namespace WarehouseGate.Domain;

public class OutwardTransaction : ITenantScoped
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }

    public int DispatchOrderId { get; set; }
    public DispatchOrder? DispatchOrder { get; set; }

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string OutwardTxnNumber { get; set; } = string.Empty;

    public OutwardStatus Status { get; set; } = OutwardStatus.PickListGenerated;

    public DateTime CreatedTime { get; set; }
    public string CreatedByOfficeUserId { get; set; } = string.Empty;

    public string? AssignedSupervisorUserId { get; set; }
    public DateTime? AssignedTime { get; set; }

    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DateTime? GateInTime { get; set; }
    public string? GateInBySecurityUserId { get; set; }
    public string? DriverName { get; set; }
    public string? DriverMobile { get; set; }
    public string? TransporterName { get; set; }
    public string? GateName { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public DateTime? GateOutTime { get; set; }
    public string? GateOutBySecurityUserId { get; set; }
    public string? GatePassToken { get; set; }

    public string? BayName { get; set; }
    public DateTime? DockInTime { get; set; }
    public DateTime? LoadingStartTime { get; set; }
    public DateTime? DockOutTime { get; set; }

    public OutwardExceptionReason? ExceptionReason { get; set; }
    public string? ExceptionRemarks { get; set; }
    public DateTime? ExceptionReportedAt { get; set; }
    public DateTime? DispatchReadyConfirmedAt { get; set; }

    // Marks when Confirm Loading Mode began for the 3D load plan (distinct from
    // LoadingStartTime, which predates the 3D plan feature and gates the older
    // flat load-lines flow).
    public DateTime? ActualLoadingStartedAt { get; set; }

    public List<OutwardPhotoEvidence> Photos { get; set; } = new();
    public List<OutwardLoadLine> LoadLines { get; set; } = new();
    public List<OutwardLoadPlanOption> LoadPlanOptions { get; set; } = new();
    public OutwardDispatchNote? DispatchNote { get; set; }
}
