namespace WarehouseGate.Domain;

public class VehicleLogisticsRecord : ITenantScoped
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }

    // Null until Office tags this dispatch plan row with a real vehicle (see
    // InwardService.TagVehicleAsync) or a legacy Excel upload/manual entry already set it directly.
    public string? VehicleNumber { get; set; }
    public string? PoNumber { get; set; }
    public string? InwardTransactionId { get; set; }
    public string? TransporterName { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public string? VehicleType { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string? SkuCode { get; set; }
    public int BoxQuantity { get; set; }

    // Office's own override of how many boxes to actually pick for this line, kept separate from
    // the Logistics Manager's planned BoxQuantity above (their Vehicle Records view is unaffected
    // by this). Null means no override - BoxQuantity is used as-is. Only settable while the row is
    // still InTransit (before pick-list generation claims it); GeneratePickListFromDispatchPlanAsync
    // uses PickListQuantity ?? BoxQuantity when building the synthesized DispatchOrderLine.
    public int? PickListQuantity { get; set; }

    public DateTime? DepartureDate { get; set; }
    public DateTime? EtaDateTime { get; set; }

    public int FromWarehouseId { get; set; }
    public Warehouse? FromWarehouse { get; set; }

    public int ToWarehouseId { get; set; }
    public Warehouse? ToWarehouse { get; set; }

    // Defaults to InTransit on creation (manual add or Excel import). InProgress is set the
    // moment this row is claimed by a real Inward/Outward job (gate check-in synthesizes a
    // PurchaseOrder from it; pick-list generation synthesizes a DispatchOrder) - see
    // ConsumedByInwardTransactionId/ConsumedByOutwardTransactionId below. Completed is set once
    // that job finishes - see InwardService/OutwardService's VehicleLogisticsSyncService calls.
    // Inactive/Deleted are manager-driven only (Deleted is a soft-delete, set by the record's
    // own Delete action instead of removing the row).
    public VehicleLogisticsStatus Status { get; set; } = VehicleLogisticsStatus.InTransit;

    // System-computed link to whichever real job this row was claimed by - distinct from the
    // InwardTransactionId string above, which is a free-text field the Logistics Manager types
    // in themselves on the Add/Edit form. A single row represents one physical shipment leg, so
    // in the normal sequence BOTH get set over the row's life: Outward claims it first (dispatch
    // from the From warehouse), then once the vehicle physically arrives, Inward claims the same
    // row again (receipt at the To warehouse) - see InwardClaimStartedAtUtc below for why that
    // second claim needs its own atomic gate distinct from Status.
    public int? ConsumedByInwardTransactionId { get; set; }
    public InwardTransaction? ConsumedByInwardTransaction { get; set; }

    public int? ConsumedByOutwardTransactionId { get; set; }
    public OutwardTransaction? ConsumedByOutwardTransaction { get; set; }

    // Dedicated atomic mutex for the Inward-side claim in InwardService.TryClaimDispatchPlanForInwardAsync.
    // Status alone can't serve this purpose here: by the time Inward claims, Status may already be
    // InProgress (set by an earlier Outward claim of the same row), so a plain "flip from X to Y and
    // check affected-row-count" guard can't tell two concurrent Inward claims apart. This field starts
    // null in every case and is set exactly once, giving that same affected-row-count guard something
    // reliable to gate on regardless of which claim (Outward, Inward, or both) touched the row first.
    public DateTime? InwardClaimStartedAtUtc { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
