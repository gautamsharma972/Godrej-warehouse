using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace WarehouseGate.Web.Services;

// Mirrors mobile/WarehouseGate.Mobile/Services/ApiClient.cs's conventions (typed client, JSON Web
// defaults, ApiException on non-success) - the server-side equivalent, reading the JWT from the
// current user's "api_token" claim (stashed there at login, see Program.cs's /login-handler)
// instead of a static field, since a Blazor Server app has many concurrent circuits/users.
public class PortalApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<PortalApiClient> _logger;

    public PortalApiClient(IHttpClientFactory httpClientFactory, AuthenticationStateProvider authStateProvider, ILogger<PortalApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authStateProvider = authStateProvider;
        _logger = logger;
    }

    private async Task<HttpClient> CreateClientAsync()
    {
        var client = _httpClientFactory.CreateClient("Api");
        var state = await _authStateProvider.GetAuthenticationStateAsync();
        var token = state.User.FindFirst("api_token")?.Value;
        if (!string.IsNullOrEmpty(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    public async Task<T> GetAsync<T>(string path) => await SendAsync<T>(HttpMethod.Get, path, null);
    public async Task<T> PostAsync<T>(string path, object body) => await SendAsync<T>(HttpMethod.Post, path, body);
    public async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken) =>
        await SendAsync<T>(HttpMethod.Post, path, body, cancellationToken);
    public async Task<T> PutAsync<T>(string path, object body) => await SendAsync<T>(HttpMethod.Put, path, body);
    public async Task PutAsync(string path, object body) => await SendAsync<object?>(HttpMethod.Put, path, body);
    public async Task DeleteAsync(string path) => await SendAsync<object?>(HttpMethod.Delete, path, null);

    public async Task<T> PostFormAsync<T>(string path, MultipartFormDataContent content)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("API call {Method} {Path} starting", "POST(form)", path);

        var client = await CreateClientAsync();
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(path, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API call {Method} {Path} failed after {ElapsedMs}ms", "POST(form)", path, stopwatch.ElapsedMilliseconds);
            throw new ApiException("Could not reach the server. Check your connection and try again.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await ExtractErrorMessageAsync(response);
            _logger.LogError("API call {Method} {Path} failed {StatusCode} in {ElapsedMs}ms: {ErrorMessage}",
                "POST(form)", path, (int)response.StatusCode, stopwatch.ElapsedMilliseconds, errorMessage);
            throw new ApiException(errorMessage);
        }

        _logger.LogInformation("API call {Method} {Path} completed {StatusCode} in {ElapsedMs}ms",
            "POST(form)", path, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return result is null ? default! : result;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("API call {Method} {Path} starting", method, path);

        var client = await CreateClientAsync();
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "API call {Method} {Path} was cancelled after {ElapsedMs}ms", method, path, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API call {Method} {Path} failed after {ElapsedMs}ms", method, path, stopwatch.ElapsedMilliseconds);
            throw new ApiException("Could not reach the server. Check your connection and try again.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await ExtractErrorMessageAsync(response);
            _logger.LogError("API call {Method} {Path} failed {StatusCode} in {ElapsedMs}ms: {ErrorMessage}",
                method, path, (int)response.StatusCode, stopwatch.ElapsedMilliseconds, errorMessage);
            throw new ApiException(errorMessage);
        }

        _logger.LogInformation("API call {Method} {Path} completed {StatusCode} in {ElapsedMs}ms",
            method, path, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

        if (typeof(T) == typeof(object))
        {
            return default!;
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result is null ? default! : result;
    }

    private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<ApiErrorPayload>(JsonOptions);
            if (!string.IsNullOrWhiteSpace(payload?.Message))
            {
                return payload.Message;
            }
        }
        catch (Exception)
        {
            // Body wasn't the expected { message } shape - fall through to the generic message.
        }

        return $"Request failed ({(int)response.StatusCode}).";
    }

    private record ApiErrorPayload(string? Message);
}
