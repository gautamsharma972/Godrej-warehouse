using Microsoft.AspNetCore.SignalR.Client;
using WarehouseGate.Mobile.Models;

namespace WarehouseGate.Mobile.Services;

public static class SupervisorHubClient
{
    private static HubConnection? _connection;

    public static event Action<InwardJob>? JobAvailable;
    public static event Action<InwardJob>? JobClaimed;
    public static event Action<InwardJob>? JobUpdated;
    public static event Action<InwardJob>? JobAssignedToYou;

    public static event Action<OutwardJob>? OutwardJobAvailable;
    public static event Action<OutwardJob>? OutwardJobClaimed;
    public static event Action<OutwardJob>? OutwardJobUpdated;
    public static event Action<OutwardJob>? OutwardJobAssignedToYou;

    // Drives the "Live" / "Reconnecting" indicator on the Supervisor home screen.
    public static event Action<bool>? ConnectionStateChanged;
    public static bool IsConnected { get; private set; }

    // Safe to fire-and-forget: realtime is additive, so a hub that can't connect must never
    // block or fail the login flow that kicks this off. WithAutomaticReconnect only takes over
    // AFTER a successful first connect, so the initial attempt gets its own small retry loop
    // here; if all attempts fail, the connection is torn down so a later StartAsync (e.g. next
    // login) starts fresh instead of returning early against a dead, never-started connection.
    public static async Task StartAsync()
    {
        if (_connection is not null)
        {
            return;
        }

        var connection = new HubConnectionBuilder()
            .WithUrl($"{AppConfig.ApiBaseUrl}/hubs/inward", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(Session.Token);
            })
            .WithAutomaticReconnect()
            .Build();
        _connection = connection;

        connection.On<InwardJob>("JobAvailable", job => JobAvailable?.Invoke(job));
        connection.On<InwardJob>("JobClaimed", job => JobClaimed?.Invoke(job));
        connection.On<InwardJob>("JobUpdated", job => JobUpdated?.Invoke(job));
        connection.On<InwardJob>("JobAssignedToYou", job => JobAssignedToYou?.Invoke(job));

        connection.On<OutwardJob>("OutwardJobAvailable", job => OutwardJobAvailable?.Invoke(job));
        connection.On<OutwardJob>("OutwardJobClaimed", job => OutwardJobClaimed?.Invoke(job));
        connection.On<OutwardJob>("OutwardJobUpdated", job => OutwardJobUpdated?.Invoke(job));
        connection.On<OutwardJob>("OutwardJobAssignedToYou", job => OutwardJobAssignedToYou?.Invoke(job));

        connection.Reconnecting += _ => { SetConnected(false); return Task.CompletedTask; };
        connection.Reconnected += _ => { SetConnected(true); return Task.CompletedTask; };
        connection.Closed += _ => { SetConnected(false); return Task.CompletedTask; };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await connection.StartAsync();
                SetConnected(true);
                return;
            }
            catch when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
            catch
            {
                // Out of attempts - tear down so the next StartAsync call can try again from
                // scratch. Pages keep working via their normal fetch/refresh flows meanwhile.
                _connection = null;
                await connection.DisposeAsync();
                SetConnected(false);
            }
        }
    }

    public static async Task StopAsync()
    {
        if (_connection is null)
        {
            return;
        }

        await _connection.StopAsync();
        await _connection.DisposeAsync();
        _connection = null;
        SetConnected(false);
    }

    private static void SetConnected(bool connected)
    {
        IsConnected = connected;
        ConnectionStateChanged?.Invoke(connected);
    }
}
