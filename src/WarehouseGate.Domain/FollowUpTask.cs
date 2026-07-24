namespace WarehouseGate.Domain;

public enum FollowUpType
{
    // GRN completed with damaged/short/excess/mismatch inspection lines - supplier follow-up.
    InwardException,
    // Dispatch note issued for a partial load - a stock transfer-out note is required.
    PartialLoadDispatch
}

public enum FollowUpStatus
{
    Open,
    Resolved
}

// An actionable Office to-do created automatically when a completed job needs human follow-up
// (the "Phase 3/4" items that previously only logged a server warning nobody saw). Scoped to a
// warehouse like the transactions themselves; EntityName/EntityId point back at the source
// InwardTransaction/OutwardTransaction the same way AuditLog rows do.
public class FollowUpTask
{
    public int Id { get; set; }

    public FollowUpType Type { get; set; }
    public FollowUpStatus Status { get; set; } = FollowUpStatus.Open;

    public string EntityName { get; set; } = string.Empty;
    public int EntityId { get; set; }

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public string? ResolvedByUserId { get; set; }
    public string? ResolvedByName { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolutionNotes { get; set; }
}
