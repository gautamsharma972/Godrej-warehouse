using System.Collections.Concurrent;

namespace WarehouseGate.Api.Assistant;

// The enforcement mechanism behind every "preview, then create" action tool (see
// Plugins/DispatchPlanCreationPlugin for the first one). A preview_* tool validates the user's
// request and stores the EXACT validated payload here under a server-generated token; the
// matching create_* tool can only act on a token it's handed back, never on fields the model
// re-states itself - so the model can't (accidentally or otherwise) create something with
// different values than what was actually shown to the user for confirmation. Deliberately a
// singleton in-memory store, not a DB table: these are short-lived (a few minutes, spanning at
// most a couple of chat turns), and losing them on an app restart just means "ask again," not
// data loss.
public class PendingActionStore
{
    private sealed record Entry(object Payload, DateTime ExpiresAtUtc);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public Guid Store<T>(T payload, TimeSpan timeToLive) where T : notnull
    {
        PurgeExpired();
        var token = Guid.NewGuid();
        _entries[token] = new Entry(payload, DateTime.UtcNow.Add(timeToLive));
        return token;
    }

    // Single-use by design - a confirmation token is consumed the moment it's redeemed, so the
    // same "yes" can't accidentally create the same record twice.
    public bool TryTake<T>(Guid token, out T payload) where T : notnull
    {
        if (_entries.TryRemove(token, out var entry) && entry.ExpiresAtUtc > DateTime.UtcNow && entry.Payload is T typed)
        {
            payload = typed;
            return true;
        }

        payload = default!;
        return false;
    }

    private void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, entry) in _entries)
        {
            if (entry.ExpiresAtUtc <= now)
            {
                _entries.TryRemove(key, out _);
            }
        }
    }
}
