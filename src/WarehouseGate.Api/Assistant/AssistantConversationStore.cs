using System.Collections.Concurrent;

namespace WarehouseGate.Api.Assistant;

// Server-owned conversation memory. The browser still sends its visible transcript as a recovery
// aid, but model context comes from this store so structured UI results can be retained without
// exposing that internal context in chat bubbles.
public sealed class AssistantConversationStore
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    private sealed record StoredTurn(string Role, string VisibleContent, string ModelContent);

    private sealed class Session
    {
        public required string UserId { get; init; }
        public DateTime ExpiresAtUtc { get; set; }
        public List<StoredTurn> Turns { get; } = [];
        public object SyncRoot { get; } = new();
    }

    public sealed record Conversation(Guid Id, IReadOnlyList<AssistantChatTurn> History);

    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    public Conversation GetOrCreate(
        Guid? requestedId,
        string userId,
        IReadOnlyList<AssistantChatTurn>? visibleHistory)
    {
        PurgeExpired();

        var id = requestedId is { } candidate &&
                 _sessions.TryGetValue(candidate, out var existing) &&
                 existing.UserId == userId
            ? candidate
            : CreateSession(userId);

        var session = _sessions[id];
        lock (session.SyncRoot)
        {
            session.ExpiresAtUtc = DateTime.UtcNow.Add(SessionLifetime);
            SynchronizeVisibleHistory(session, visibleHistory ?? []);

            return new Conversation(
                id,
                session.Turns
                    .Select(t => new AssistantChatTurn(t.Role, t.ModelContent))
                    .ToList());
        }
    }

    public void AppendExchange(
        Guid conversationId,
        string userId,
        string userMessage,
        string visibleAssistantReply,
        string modelAssistantContext)
    {
        if (!_sessions.TryGetValue(conversationId, out var session) || session.UserId != userId)
        {
            return;
        }

        lock (session.SyncRoot)
        {
            session.ExpiresAtUtc = DateTime.UtcNow.Add(SessionLifetime);
            session.Turns.Add(new StoredTurn("user", userMessage, userMessage));
            session.Turns.Add(new StoredTurn("assistant", visibleAssistantReply, modelAssistantContext));
        }
    }

    private Guid CreateSession(string userId)
    {
        var id = Guid.NewGuid();
        _sessions[id] = new Session
        {
            UserId = userId,
            ExpiresAtUtc = DateTime.UtcNow.Add(SessionLifetime)
        };
        return id;
    }

    // Direct form/confirmation responses are currently added by the widget without a chat request.
    // On the next chat turn, import any visible suffix that the server has not seen. If the browser
    // transcript diverges (for example after a future Clear action), rebuild from that point.
    private static void SynchronizeVisibleHistory(
        Session session,
        IReadOnlyList<AssistantChatTurn> visibleHistory)
    {
        var commonCount = 0;
        var comparableCount = Math.Min(session.Turns.Count, visibleHistory.Count);
        while (commonCount < comparableCount &&
               session.Turns[commonCount].Role == NormalizeRole(visibleHistory[commonCount].Role) &&
               session.Turns[commonCount].VisibleContent == visibleHistory[commonCount].Content)
        {
            commonCount++;
        }

        if (commonCount < session.Turns.Count)
        {
            session.Turns.RemoveRange(commonCount, session.Turns.Count - commonCount);
        }

        for (var i = commonCount; i < visibleHistory.Count; i++)
        {
            var turn = visibleHistory[i];
            var role = NormalizeRole(turn.Role);
            session.Turns.Add(new StoredTurn(role, turn.Content, turn.Content));
        }
    }

    private static string NormalizeRole(string role) =>
        string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";

    private void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (id, session) in _sessions)
        {
            if (session.ExpiresAtUtc <= now)
            {
                _sessions.TryRemove(id, out _);
            }
        }
    }
}
