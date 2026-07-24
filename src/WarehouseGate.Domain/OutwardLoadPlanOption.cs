namespace WarehouseGate.Domain;

// One saved 3D loading arrangement ("Option A/B/C") a supervisor built for an
// outward job. Utilization/weight/balance stats are intentionally not stored
// here - they're recomputed at read time from the Groups, same "stateless
// recompute" convention already used by the existing auto-plan endpoint.
public class OutwardLoadPlanOption
{
    public int Id { get; set; }

    public int OutwardTransactionId { get; set; }
    public OutwardTransaction? OutwardTransaction { get; set; }

    public string Label { get; set; } = string.Empty;
    public bool IsSelected { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;

    public List<OutwardLoadPlanGroup> Groups { get; set; } = new();
}
