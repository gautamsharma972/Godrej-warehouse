using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MudBlazor.Services;
using WarehouseGate.Web.Components;
using WarehouseGate.Web.Services;

var builder = WebApplication.CreateBuilder(args);

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
        response = await client.PostAsJsonAsync("api/auth/login", new { userName, password });
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

app.MapPost("/logout-handler", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
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
