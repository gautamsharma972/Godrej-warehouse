using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;

namespace WarehouseGate.Web.Services;

// Logistics Manager's equivalent of OfficeRealtimeClient - a second SignalR connection (distinct
// from Blazor Server's own circuit connection) to the shared InwardHub, so the Dispatch Plan page
// picks up changes made by other Logistics Managers (or via Excel upload) without a manual
// refresh. Scoped, not static - each Blazor circuit is a different user's browser tab.
public class LogisticsRealtimeClient : IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly AuthenticationStateProvider _authStateProvider;
    private HubConnection? _connection;

    // Deliberately just a "something changed, go re-fetch" signal - the page already re-fetches
    // its full authoritative list after any local action, so hub-triggered refreshes reuse that
    // same LoadAsync path.
    public event Action? VehicleLogisticsRecordChanged;

    public LogisticsRealtimeClient(IConfiguration configuration, AuthenticationStateProvider authStateProvider)
    {
        _configuration = configuration;
        _authStateProvider = authStateProvider;
    }

    public async Task StartAsync()
    {
        if (_connection is not null)
        {
            return;
        }

        var apiBaseUrl = _configuration["ApiBaseUrl"] ?? "http://localhost:5080/";
        var hubUrl = new Uri(new Uri(apiBaseUrl), "hubs/inward").ToString();

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    var state = await _authStateProvider.GetAuthenticationStateAsync();
                    return state.User.FindFirst("api_token")?.Value;
                };
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On("VehicleLogisticsRecordChanged", () => VehicleLogisticsRecordChanged?.Invoke());

        try
        {
            await _connection.StartAsync();
        }
        catch
        {
            // Real-time is additive, not load-bearing - a page whose hub connection can't start
            // (API briefly down, etc.) still works via its normal manual refresh/action flow.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
