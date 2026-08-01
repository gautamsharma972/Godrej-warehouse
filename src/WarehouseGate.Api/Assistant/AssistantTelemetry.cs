using System.Collections.Concurrent;
using WarehouseGate.Api.Dtos;

namespace WarehouseGate.Api.Assistant;

// Privacy-conscious operational telemetry: prompt and reply text are deliberately never stored.
// This process-local implementation is immediately useful in development and exposes a stable
// contract that can later be backed by durable metrics storage without changing the widget.
public sealed class AssistantTelemetry
{
    private const int MaxRecentTurns = 5_000;

    private sealed class TurnMetric
    {
        public required Guid Id { get; init; }
        public required string UserId { get; init; }
        public required string Source { get; init; }
        public string? CapabilityId { get; init; }
        public required bool Success { get; init; }
        public required long LatencyMs { get; init; }
        public bool? Helpful { get; set; }
    }

    private readonly DateTime _sinceUtc = DateTime.UtcNow;
    private readonly ConcurrentDictionary<Guid, TurnMetric> _turns = new();
    private readonly ConcurrentQueue<Guid> _turnOrder = new();
    private readonly ConcurrentDictionary<string, int> _workflowOutcomes = new();
    private readonly ILogger<AssistantTelemetry> _logger;

    public AssistantTelemetry(ILogger<AssistantTelemetry> logger)
    {
        _logger = logger;
    }

    public Guid RecordTurn(
        string userId,
        string source,
        string? capabilityId,
        bool success,
        long latencyMs)
    {
        var id = Guid.NewGuid();
        _turns[id] = new TurnMetric
        {
            Id = id,
            UserId = userId,
            Source = source,
            CapabilityId = capabilityId,
            Success = success,
            LatencyMs = latencyMs
        };
        _turnOrder.Enqueue(id);
        Trim();

        _logger.LogInformation(
            "Assistant turn {TurnId}: source={Source}, capability={CapabilityId}, success={Success}, latencyMs={LatencyMs}",
            id,
            source,
            capabilityId ?? "free-form",
            success,
            latencyMs);
        return id;
    }

    public bool RecordFeedback(Guid turnId, string userId, bool helpful)
    {
        if (!_turns.TryGetValue(turnId, out var turn) || turn.UserId != userId)
        {
            return false;
        }

        turn.Helpful = helpful;
        _logger.LogInformation("Assistant feedback for turn {TurnId}: helpful={Helpful}", turnId, helpful);
        return true;
    }

    public void RecordWorkflow(string actionType, string stage, bool success)
    {
        var key = $"{actionType}.{stage}.{(success ? "success" : "rejected")}";
        _workflowOutcomes.AddOrUpdate(key, 1, (_, count) => count + 1);
        _logger.LogInformation(
            "Assistant workflow: action={ActionType}, stage={Stage}, success={Success}",
            actionType,
            stage,
            success);
    }

    public AssistantMetricsDto Snapshot()
    {
        var turns = _turns.Values.ToList();
        return new AssistantMetricsDto(
            _sinceUtc,
            turns.Count,
            turns.Count(t => t.Source == "model"),
            turns.Count(t => t.Source == "capability"),
            turns.Count(t => t.Success),
            turns.Count(t => !t.Success),
            turns.Count(t => t.Helpful == true),
            turns.Count(t => t.Helpful == false),
            turns.Count == 0 ? 0 : turns.Average(t => t.LatencyMs),
            turns
                .Where(t => t.CapabilityId is not null)
                .GroupBy(t => t.CapabilityId!)
                .ToDictionary(g => g.Key, g => g.Count()),
            new Dictionary<string, int>(_workflowOutcomes));
    }

    private void Trim()
    {
        while (_turns.Count > MaxRecentTurns && _turnOrder.TryDequeue(out var oldest))
        {
            _turns.TryRemove(oldest, out _);
        }
    }
}
