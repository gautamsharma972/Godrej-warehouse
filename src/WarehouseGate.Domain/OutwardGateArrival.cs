namespace WarehouseGate.Domain;

// A vehicle's physical outward gate arrival, logged by Security independent of any Dispatch Plan
// / pick list (mirrors InwardTransaction's GateIn-stage fields). OutwardTransaction.DispatchOrderId
// is a non-nullable FK used with '!' throughout load planning/reports/assistant plugins, so this is
// deliberately a separate table rather than a nullable-DispatchOrderId OutwardTransaction row -
// Office's "Link Vehicle" action (OutwardService.LinkVehicleAsync) copies this arrival's
// vehicle/driver/photos onto an already-pick-listed OutwardTransaction and stamps it linked below.
public class OutwardGateArrival : ITenantScoped
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DateTime GateInTime { get; set; }
    public string GateInBySecurityUserId { get; set; } = string.Empty;

    public string? DriverName { get; set; }
    public string? DriverMobile { get; set; }
    public string? TransporterName { get; set; }
    public string? GateName { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }

    // Whatever Dispatch Order Number Security typed on the Outward form - purely informational
    // (not validated against any pick list), kept as a hint for Office when picking which pending
    // pick list to link this arrival to.
    public string? SecurityEnteredDispatchOrderNumber { get; set; }

    // Null until Office links this arrival to a pick-listed OutwardTransaction.
    public int? LinkedOutwardTransactionId { get; set; }
    public OutwardTransaction? LinkedOutwardTransaction { get; set; }
    public DateTime? LinkedAtUtc { get; set; }
    public string? LinkedByOfficeUserId { get; set; }

    public List<OutwardGateArrivalPhoto> Photos { get; set; } = new();
}
