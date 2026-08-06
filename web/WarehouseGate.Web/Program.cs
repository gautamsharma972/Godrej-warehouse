using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MudBlazor.Services;
using Serilog;
using WarehouseGate.Web.Components;
using WarehouseGate.Web.Services;

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

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddHttpClient("Api", client =>
    {
        var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5080/";
        client.BaseAddress = new Uri(apiBaseUrl);
        // 120s, not 20s - api/assistant/chat's tool-calling round trip runs the self-hosted CPU
        // model twice (decide whether to call a tool, then compose the final answer from its
        // result). Response time scales with how much text a tool result gives it to reproduce, not
        // just read - measured ~15s for a 1-line result but ~97s for a real region's full in-transit
        // list before LogisticsPlugin capped its detail - so this stays generous even after that cap.
        // Harmless for every other endpoint, which returns in milliseconds regardless of the ceiling.
        client.Timeout = TimeSpan.FromSeconds(120);
    });
    builder.Services.AddScoped<PortalApiClient>();
    builder.Services.AddScoped<OfficeRealtimeClient>();
    builder.Services.AddScoped<LogisticsRealtimeClient>();
    builder.Services.AddScoped<DashboardRealtimeClient>();

    builder.Services.AddCascadingAuthenticationState();
    builder.Services
        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login";
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = true;

            // The cookie middleware issues this redirect server-side, before Blazor's own router
            // (and RedirectToLogin.razor's path-aware logic) ever runs - so an unauthenticated request
            // for a /platform/* URL needs its own redirect target here, not just in the Blazor router.
            var redirectToLogin = options.Events.OnRedirectToLogin;
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/platform"))
                {
                    var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                    context.Response.Redirect($"/platform/login?ReturnUrl={returnUrl}");
                    return Task.CompletedTask;
                }
                return redirectToLogin(context);
            };

            // Fires when the caller IS authenticated but lacks the required role (e.g. a tenant
            // SuperAdmin hitting /platform/*, or a PlatformAdmin hitting a tenant-only page) - route
            // each back to whichever sign-in screen actually matches the account they're on.
            options.Events.OnRedirectToAccessDenied = context =>
            {
                var landing = context.HttpContext.User.IsInRole("PlatformAdmin") ? "/platform/login" : "/login";
                context.Response.Redirect(landing);
                return Task.CompletedTask;
            };
        });
    builder.Services.AddAuthorization();

    builder.Services.AddMudServices();

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }
    app.UseStatusCodePagesWithReExecute("/not-found");
    app.UseStaticFiles();
    app.UseHttpsRedirection();

    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, elapsedMs, ex) => ex is not null || httpContext.Response.StatusCode >= 500
            ? Serilog.Events.LogEventLevel.Error
            : httpContext.Response.StatusCode >= 400
                ? Serilog.Events.LogEventLevel.Warning
                : Serilog.Events.LogEventLevel.Information;
    });

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    // Plain (non-Blazor) endpoints for sign-in/out: HttpContext.SignInAsync needs to write a
    // Set-Cookie response header on the original HTTP request/response, which isn't available from
    // inside an already-established Interactive Server circuit - so the login form posts here as a
    // regular HTML form submit rather than through Blazor's own event/data binding.
    app.MapPost("/login-handler", async (HttpContext http, IHttpClientFactory httpClientFactory) =>
    {
        var form = await http.Request.ReadFormAsync();
        var orgCode = form["orgCode"].ToString();
        var userName = form["userName"].ToString();
        var password = form["password"].ToString();

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return Results.Redirect("/login?error=invalid");
        }

        var client = httpClientFactory.CreateClient("Api");
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("api/auth/login", new { userName, password, organizationCode = orgCode });
        }
        catch (Exception)
        {
            return Results.Redirect("/login?error=connect");
        }

        if (!response.IsSuccessStatusCode)
        {
            return Results.Redirect("/login?error=invalid");
        }

        var login = await response.Content.ReadFromJsonAsync<LoginApiResponse>();
        if (login is null)
        {
            return Results.Redirect("/login?error=invalid");
        }

        if (login.Role is not ("SuperAdmin" or "LogisticsManager" or "Office"))
        {
            return Results.Redirect("/login?error=role");
        }

        // The JWT already carries NameIdentifier/Name/Role/displayName claims (see TokenService in
        // the API) - copy them straight into the cookie identity rather than re-deriving them, and
        // stash the raw token too so PortalApiClient can attach it as a Bearer header on API calls.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        var identity = new ClaimsIdentity(jwt.Claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim("api_token", login.Token));

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        var landing = login.Role switch
        {
            "SuperAdmin" => "/admin/dashboard",
            "LogisticsManager" => "/logistics/dashboard",
            _ => "/office/dashboard"
        };
        return Results.Redirect(landing);
    });

    // Mirrors /login-handler above, for the platform site instead of a tenant's own portal - no
    // Organization Code (PlatformAdmin isn't scoped to one) and a different allowed-role/landing page.
    app.MapPost("/platform-login-handler", async (HttpContext http, IHttpClientFactory httpClientFactory) =>
    {
        var form = await http.Request.ReadFormAsync();
        var userName = form["userName"].ToString();
        var password = form["password"].ToString();

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return Results.Redirect("/platform/login?error=invalid");
        }

        var client = httpClientFactory.CreateClient("Api");
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("api/auth/login", new { userName, password });
        }
        catch (Exception)
        {
            return Results.Redirect("/platform/login?error=connect");
        }

        if (!response.IsSuccessStatusCode)
        {
            return Results.Redirect("/platform/login?error=invalid");
        }

        var login = await response.Content.ReadFromJsonAsync<LoginApiResponse>();
        if (login is null || login.Role != "PlatformAdmin")
        {
            return Results.Redirect("/platform/login?error=role");
        }

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        var identity = new ClaimsIdentity(jwt.Claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim("api_token", login.Token));

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect("/platform/organizations");
    });

    app.MapPost("/logout-handler", async (HttpContext http) =>
    {
        var isPlatformAdmin = http.User.IsInRole("PlatformAdmin");
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect(isPlatformAdmin ? "/platform/login" : "/login");
    });

    // Browser <img> tags can't attach a Bearer header, so this proxies photo/document requests
    // server-side using the api_token claim from the authenticated cookie - same reasoning as
    // /login-handler being a plain endpoint rather than Blazor-bound.
    app.MapGet("/file-proxy/{**path}", async (HttpContext http, string path, IHttpClientFactory httpClientFactory) =>
    {
        if (http.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        var token = http.User.FindFirst("api_token")?.Value;
        if (string.IsNullOrEmpty(token))
        {
            return Results.Unauthorized();
        }

        var client = httpClientFactory.CreateClient("Api");
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/files/{path}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return Results.File(bytes, contentType);
    }).RequireAuthorization();

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

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
