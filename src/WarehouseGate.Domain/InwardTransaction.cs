namespace WarehouseGate.Domain;

public class InwardTransaction : ITenantScoped
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string InwardTxnNumber { get; set; } = string.Empty;

    // Null from Security's Gate Check-in until Office links this arrival to a real Dispatch Plan
    // entry (see InwardService.LinkVehicleAsync) - Security no longer needs a matching PO to exist
    // before recording an arrival at all.
    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    // Whatever PO Number Security typed on the Gate Check-in form - purely informational (not
    // validated against Dispatch Plan data), kept as a hint for Office when picking which Dispatch
    // Plan entry to link this arrival to.
    public string? SecurityEnteredPoNumber { get; set; }

    public InwardStatus Status { get; set; } = InwardStatus.GateIn;

    public DateTime GateInTime { get; set; }
    public string GateInBySecurityUserId { get; set; } = string.Empty;

    public string? DriverName { get; set; }
    public string? DriverMobile { get; set; }
    public string? TransporterName { get; set; }
    public string? GateName { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }

    public bool IsNewVehicle { get; set; }
    public bool HasDeliveryDateMismatch { get; set; }
    public string? Remarks { get; set; }

    public string? AssignedSupervisorUserId { get; set; }
    public DateTime? AssignedTime { get; set; }

    public string? BayName { get; set; }
    public DateTime? DockInTime { get; set; }
    public DateTime? UnloadingStartTime { get; set; }
    public DateTime? DockOutTime { get; set; }

    public DateTime? GateOutTime { get; set; }
    public string? GateOutBySecurityUserId { get; set; }
    public string? GatePassToken { get; set; }

    public List<PhotoEvidence> Photos { get; set; } = new();
    public List<InwardDocument> Documents { get; set; } = new();
    public List<InspectionLine> InspectionLines { get; set; } = new();
    public List<UnplannedReceiptLine> UnplannedLines { get; set; } = new();
    public GoodsReceiptNote? Grn { get; set; }
}
