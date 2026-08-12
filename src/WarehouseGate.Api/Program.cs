using System.Text;
using Amazon;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using WarehouseGate.Api;
using WarehouseGate.Api.Assistant;
using WarehouseGate.Api.Hubs;
using WarehouseGate.Api.Services;
using WarehouseGate.Infrastructure;
using WarehouseGate.Infrastructure.Storage;
using WarehouseGate.LoadPlanning;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
        ?? throw new InvalidOperationException("Jwt configuration section is missing.");
    builder.Services.AddSingleton(jwtOptions);

    // "S3" in production (bucket/region only - no access keys live here; the AWS SDK's default
    // credential chain picks up the EC2/ECS instance role automatically), "LocalDisk" (default) for
    // local dev so it keeps working with zero AWS setup at all.
    var storageProvider = builder.Configuration["Storage:Provider"] ?? "LocalDisk";
    if (string.Equals(storageProvider, "S3", StringComparison.OrdinalIgnoreCase))
    {
        var s3Options = builder.Configuration.GetSection("Storage:S3").Get<S3PhotoStorageOptions>()
            ?? throw new InvalidOperationException("Storage:S3 configuration section is missing.");
        builder.Services.AddSingleton(s3Options);
        builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(RegionEndpoint.GetBySystemName(s3Options.Region)));
        builder.Services.AddSingleton<IPhotoStorageService, S3PhotoStorageService>();
    }
    else
    {
        var photoStorageOptions = builder.Configuration.GetSection("PhotoStorage").Get<LocalDiskPhotoStorageOptions>()
            ?? new LocalDiskPhotoStorageOptions();
        builder.Services.AddSingleton(photoStorageOptions);
        builder.Services.AddSingleton<IPhotoStorageService, LocalDiskPhotoStorageService>();
    }

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentTenantProvider, HttpContextCurrentTenantProvider>();

    var connectionString = builder.Configuration.GetConnectionString("Default");
    builder.Services.AddDbContext<WarehouseGateDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
            mySqlOptions => mySqlOptions.EnableRetryOnFailure()));

    builder.Services
        .AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequiredLength = 6;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<WarehouseGateDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

    builder.Services.AddScoped<TokenService>(_ => new TokenService(jwtOptions));
    builder.Services.AddScoped<InwardService>();
    builder.Services.AddScoped<OutwardLoadPlanService>();
    builder.Services.AddScoped<OutwardService>();
    builder.Services.AddScoped<AuditService>();
    builder.Services.AddScoped<VehicleLogisticsSyncService>();
    builder.Services.AddSingleton<LoadPlanningEngine>();
    builder.Services.AddScoped<LoadPlanningService>();
    builder.Services.AddScoped<DashboardAnalyticsService>();
    builder.Services.AddScoped<WarehouseScopeResolver>();

    // Assistant/ is a self-contained folder (Semantic Kernel + a self-hosted Ollama model) - callers
    // outside it should only ever depend on IAssistantService, never AssistantService/SK types
    // directly (see AssistantService's own header comment for why it isn't a separate project).
    var assistantOptions = builder.Configuration.GetSection("Assistant").Get<AssistantOptions>()
        ?? new AssistantOptions();
    builder.Services.AddSingleton(assistantOptions);
    builder.Services.AddSingleton<IAssistantService, AssistantService>();
    builder.Services.AddSingleton<PendingActionStore>();
    builder.Services.AddSingleton<AssistantConversationStore>();
    builder.Services.AddSingleton<AssistantTelemetry>();

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            // Browsers/native SignalR clients can't always set the Authorization header on the
            // websocket handshake, so accept the token via query string for hub connections.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) &&
                        context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
                // A token's "orgId" claim is baked in at login and never re-checked afterward - if
                // that organization is later deleted (or the token was issued against a different
                // database, e.g. during a local-to-production connection string switch), every
                // request still sails through JWT validation and only blows up much later as a raw
                // DbUpdateException the first time something auto-stamps OrganizationId onto a new
                // row (most visibly AuditService's own insert, since that's a fresh row on nearly
                // every write, even ones that don't otherwise touch OrganizationId). Rejecting here
                // - before the request ever reaches a controller/DB write - turns that into a clean
                // 401 instead. PlatformAdmin's orgId is legitimately absent (see
                // HttpContextCurrentTenantProvider's own comment on that), so no claim means nothing
                // to check, same as today.
                OnTokenValidated = async context =>
                {
                    var orgIdClaim = context.Principal?.FindFirst("orgId")?.Value;
                    if (string.IsNullOrEmpty(orgIdClaim) || !int.TryParse(orgIdClaim, out var orgId))
                    {
                        return;
                    }

                    var db = context.HttpContext.RequestServices.GetRequiredService<WarehouseGateDbContext>();
                    var organizationExists = await db.Organizations.AnyAsync(o => o.Id == orgId);
                    if (!organizationExists)
                    {
                        context.Fail("Organization no longer exists - please sign in again.");
                    }
                }
            };
        });

    builder.Services.AddAuthorization();
    builder.Services.AddSignalR();

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Paste the JWT returned from /api/auth/login."
        });
        options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // No actual browser ever calls this API cross-origin today - the web portal is Blazor Server
    // (its own server proxies every API call, including /file-proxy), and the mobile app is a
    // native HttpClient/SignalR client, neither of which CORS applies to. This policy exists purely
    // as a guardrail for whatever DOES end up calling from a browser later. Wide open
    // (SetIsOriginAllowed(_ => true) + AllowCredentials) was the previous default - locked down to
    // an explicit allowlist instead. Cors:AllowedOrigins is unset by default, which falls back to
    // just the local dev portal origins below; add the real production web portal origin(s) via
    // that config key (or the Cors__AllowedOrigins__0 env var) once that's known.
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (allowedOrigins is null || allowedOrigins.Length == 0)
    {
        allowedOrigins = ["https://localhost:7172", "http://localhost:5062"];
    }
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.AllowAnyHeader().AllowAnyMethod().WithOrigins(allowedOrigins).AllowCredentials());
    });

    // Unauthenticated brute-force guard on login specifically - Identity's own account lockout
    // isn't engaged here (AuthController.Login calls CheckPasswordAsync directly, not
    // PasswordSignInAsync), so without this a weak/guessable password has literally no attempt
    // ceiling. Partitioned per client IP so one attacker can't exhaust everyone else's budget.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("login", httpContext => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 5,
                QueueLimit = 0
            }));
    });

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<WarehouseGateDbContext>();

    var app = builder.Build();

//using (var scope = app.Services.CreateScope())
//{
//    await SeedData.InitializeAsync(scope.ServiceProvider);
//}

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // Local Android emulators call the dev API over HTTP via 10.0.2.2. Redirecting that
    // request to HTTPS breaks on the emulator because the localhost dev certificate is not trusted.
    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }
    app.UseCors();
    app.UseRateLimiter();

    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, elapsedMs, ex) => ex is not null || httpContext.Response.StatusCode >= 500
            ? Serilog.Events.LogEventLevel.Error
            : httpContext.Response.StatusCode >= 400
                ? Serilog.Events.LogEventLevel.Warning
                : Serilog.Events.LogEventLevel.Information;
    });

    app.UseExceptionHandler();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<InwardHub>("/hubs/inward");
    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
