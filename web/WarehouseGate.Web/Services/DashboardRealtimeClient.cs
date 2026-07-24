using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;

namespace WarehouseGate.Web.Services;

// Shared realtime feed for the three role dashboards (SuperAdmin/Logistics Manager/Office) - a
// second SignalR connection (distinct from Blazor Server's own circuit connection) to the same
// InwardHub, like OfficeRealtimeClient/LogisticsRealtimeClient. The dashboards recompute
// EVERYTHING they show (summary counts, leaderboard, trend charts, KPI tiles) from one fetch,
// so this collapses every hub message into a single "something changed, go re-fetch" event
// rather than distinguishing which entity moved. Scoped, not static - each Blazor circuit is a
// different user's browser tab, so each gets its own hub connection.
public class DashboardRealtimeClient : IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly AuthenticationStateProvider _authStateProvider;
    private HubConnection? _connection;

    public event Action? DashboardChanged;

    public DashboardRealtimeClient(IConfiguration configuration, AuthenticationStateProvider authStateProvider)
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

        // Every job-lifecycle event can move a dashboard number: gate-ins/pick-lists change the
        // "today" counts, claims/assignments change active-queue splits, and updates (including
        // completion) feed the leaderboard, trends, and KPI tiles.
        _connection.On<object>("JobAvailable", _ => DashboardChanged?.Invoke());
        _connection.On<object>("JobClaimed", _ => DashboardChanged?.Invoke());
        _connection.On<object>("JobUpdated", _ => DashboardChanged?.Invoke());
        _connection.On<object>("OutwardJobAvailable", _ => DashboardChanged?.Invoke());
        _connection.On<object>("OutwardJobClaimed", _ => DashboardChanged?.Invoke());
        _connection.On<object>("OutwardJobUpdated", _ => DashboardChanged?.Invoke());
        _connection.On("VehicleLogisticsRecordChanged", () => DashboardChanged?.Invoke());
        _connection.On<int>("LoadPlanChanged", _ => DashboardChanged?.Invoke());

        try
        {
            await _connection.StartAsync();
        }
        catch
        {
            // Real-time is additive, not load-bearing - a dashboard whose hub connection can't
            // start (API briefly down, etc.) still works via its normal load-on-open flow.
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
