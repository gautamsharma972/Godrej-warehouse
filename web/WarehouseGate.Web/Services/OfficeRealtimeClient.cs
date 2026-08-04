using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;

namespace WarehouseGate.Web.Services;

// Blazor Server equivalent of mobile/WarehouseGate.Mobile/Services/SupervisorHubClient.cs - a
// second SignalR connection (distinct from Blazor Server's own circuit connection) to the same
// InwardHub the mobile app already uses, so Office pages get the same live push (job available /
// claimed / assigned / updated) instead of only refreshing when the office user takes an action
// themselves. Scoped, not static - each Blazor circuit is a different office user's browser tab,
// so each gets its own hub connection (mirrors PortalApiClient's per-circuit reasoning).
public class OfficeRealtimeClient : IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly AuthenticationStateProvider _authStateProvider;
    private HubConnection? _connection;

    // Deliberately just a "something changed, go re-fetch" signal rather than passing the
    // pushed DTO through - every page that cares already re-fetches its full authoritative list
    // after any local action, so hub-triggered refreshes reuse that same LoadAsync path.
    public event Action? InwardJobChanged;
    public event Action? OutwardJobChanged;
    public event Action? SupervisorsChanged;
    public event Action? VehicleLogisticsRecordChanged;
    public event Action? FollowUpsChanged;
    public event Action? OutwardGateArrivalChanged;

    // Carries the outward transaction id (unlike the other events above) so a detail page can
    // ignore pushes for a different job than the one it's currently showing.
    public event Action<int>? LoadPlanChanged;

    public OfficeRealtimeClient(IConfiguration configuration, AuthenticationStateProvider authStateProvider)
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

        _connection.On<object>("JobAvailable", _ => InwardJobChanged?.Invoke());
        _connection.On<object>("JobClaimed", _ => InwardJobChanged?.Invoke());
        _connection.On<object>("JobUpdated", _ => InwardJobChanged?.Invoke());
        _connection.On<object>("OutwardJobAvailable", _ => OutwardJobChanged?.Invoke());
        _connection.On<object>("OutwardJobClaimed", _ => OutwardJobChanged?.Invoke());
        _connection.On<object>("OutwardJobUpdated", _ => OutwardJobChanged?.Invoke());
        _connection.On("SupervisorsChanged", () => SupervisorsChanged?.Invoke());
        _connection.On("VehicleLogisticsRecordChanged", () => VehicleLogisticsRecordChanged?.Invoke());
        _connection.On("FollowUpsChanged", () => FollowUpsChanged?.Invoke());
        _connection.On("OutwardGateArrivalChanged", () => OutwardGateArrivalChanged?.Invoke());
        _connection.On<int>("LoadPlanChanged", id => LoadPlanChanged?.Invoke(id));

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
