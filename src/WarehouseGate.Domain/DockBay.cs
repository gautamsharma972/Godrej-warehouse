namespace WarehouseGate.Domain;

// Dock/bay master per warehouse. Historically BayName on transactions was free text
// ("Bay-{1..50}"); when a warehouse defines its real bays here, the mobile dock-in screens
// offer them as a picker instead, and Dock Utilization uses the true bay count as its
// denominator instead of guessing from distinct bay-name strings.
public class DockBay : ITenantScoped
{
    public int Id { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public int OrganizationId { get; set; }
}
