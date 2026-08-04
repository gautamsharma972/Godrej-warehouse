namespace WarehouseGate.Mobile.Models;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string? WarehouseName { get; set; }
    public string? RegionName { get; set; }
}

public class PoLine
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal ExpectedQty { get; set; }
    public string UnitOfMeasure { get; set; } = string.Empty;
    // True for a SKU the source warehouse's supervisor added during loading, with no matching
    // Dispatch Plan row at all - see InwardService.ResolveDispatchQuantitiesAsync (API side).
    public bool IsExtra { get; set; }
}

public class Photo
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
    public int? PurchaseOrderLineId { get; set; }
}

// The real SKU actually received in place of an expected one - backs "Mismatch SKU Details".
// Distinct from an InspectionLine with Condition == "Mismatch", which just records how much of an
// expected PO line's quantity turned out not to be that SKU at all. See
// InwardService.SubmitInspectionAsync for the cross-validation between the two.
public class UnplannedReceiptLine
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? SkuCode { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

public class UnplannedReceiptLineInput
{
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

public class SkuMasterItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SkuCode { get; set; } = string.Empty;
}

public class InspectionLine
{
    public int Id { get; set; }
    public int PurchaseOrderLineId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal ExpectedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class Grn
{
    public string GrnNumber { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public bool HasExceptions { get; set; }
}

public class GateDocument
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}

public class InwardJob
{
    public int Id { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public string InwardTxnNumber { get; set; } = string.Empty;
    // Null until Office links this job to a Dispatch Plan entry (see the web Office app's Link
    // Vehicle action) - Security's Gate Check-in no longer creates this with a PO attached.
    public string? PONumber { get; set; }
    // Whatever PO Number Security typed at Gate Check-in - a hint only, shown until PONumber above is set for real.
    public string? SecurityEnteredPoNumber { get; set; }
    public string? SupplierName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime GateInTime { get; set; }
    public string? DriverName { get; set; }
    public string? DriverMobile { get; set; }
    public string? TransporterName { get; set; }
    public string? GateName { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public bool IsNewVehicle { get; set; }
    public bool HasDeliveryDateMismatch { get; set; }
    public string? AssignedSupervisorUserId { get; set; }
    public string? BayName { get; set; }
    public DateTime? DockInTime { get; set; }
    public DateTime? UnloadingStartTime { get; set; }
    public DateTime? DockOutTime { get; set; }
    public List<PoLine> Lines { get; set; } = new();
    public List<Photo> Photos { get; set; } = new();
    public List<GateDocument> Documents { get; set; } = new();
    public List<InspectionLine> InspectionLines { get; set; } = new();
    public List<UnplannedReceiptLine> UnplannedLines { get; set; } = new();
    public Grn? Grn { get; set; }
    public string? Remarks { get; set; }
    public DateTime? GateOutTime { get; set; }
    public string? GatePassToken { get; set; }

    private string PoAndSupplierText => PONumber is null
        ? (string.IsNullOrWhiteSpace(SecurityEnteredPoNumber) ? "Not linked to a PO yet" : $"Not linked yet (PO noted: {SecurityEnteredPoNumber})")
        : $"PO {PONumber} · {SupplierName}";

    public string Subtitle => PoAndSupplierText;
    public string SubtitleWithTime => $"{PoAndSupplierText} · {GateInTime:d MMM, h:mm tt}";
    public string WaitingCaption => Services.TimeFormat.Since(GateInTime);

    public string? TimeTrackingCaption => Status switch
    {
        "Inspecting" when UnloadingStartTime.HasValue => $"Unloading {Services.TimeFormat.Since(UnloadingStartTime.Value)}",
        "Completed" when UnloadingStartTime.HasValue && DockOutTime.HasValue =>
            $"Took {Services.TimeFormat.Duration(DockOutTime.Value - UnloadingStartTime.Value)}",
        _ => null
    };
    public bool HasTimeTrackingCaption => TimeTrackingCaption is not null;
}

public class GateCheckInInput
{
    public string VehicleNumber { get; set; } = string.Empty;
    public string InwardTxnNumber { get; set; } = string.Empty;
    public string PONumber { get; set; } = string.Empty;
    public string? DriverName { get; set; }
    public string? DriverMobile { get; set; }
    public string? TransporterName { get; set; }
    public string? GateName { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public string? Remarks { get; set; }
}

public class InspectionLineInput
{
    public int PurchaseOrderLineId { get; set; }
    public decimal ReceivedQty { get; set; }
    public string Condition { get; set; } = "Ok";
    public string? Notes { get; set; }
}

public class ApiError
{
    public string? Message { get; set; }
}

// Read-only reference to the original Outward job that dispatched this same shipment (if any) -
// see the API-side InwardOutwardReferenceDto for the full explanation of when this exists.
public class InwardOutwardReference
{
    public bool Exists { get; set; }
    public int? OutwardTransactionId { get; set; }
    public string? DispatchOrderNumber { get; set; }
    public string? CustomerName { get; set; }
    public string? VehicleNumber { get; set; }
    public double? VehicleWidthCm { get; set; }
    public double? VehicleLengthCm { get; set; }
    public double? VehicleHeightCm { get; set; }
    public double? VehicleMaxWeightKg { get; set; }
    public List<LoadPlanGroup> Groups { get; set; } = new();
}

public class VehicleMasterDto
{
    public string VehicleNumber { get; set; } = string.Empty;
    public string? DriverName { get; set; }
    public string? DriverMobile { get; set; }
    public string? TransporterName { get; set; }
    public string? DispatchOrderNumber { get; set; }
}

// Display-only: one distinct vehicle, summarized from its (possibly several) ExpectedShipment
// rows - built client-side for the vehicle picker, never sent to/from the server.
public class VehicleOption
{
    public string VehicleNumber { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

// One row per SKU line from the Logistics Manager's pre-registered shipment upload - a single
// vehicle can appear multiple times here, sometimes sharing a PO/inward-txn pair (multiple SKUs
// under one delivery), sometimes with a genuinely different pair (multiple POs, one vehicle).
public class ExpectedShipment
{
    public int Id { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public string? PoNumber { get; set; }
    public string? InwardTransactionId { get; set; }
    public string? TransporterName { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public string? VehicleType { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? SkuCode { get; set; }
    public int BoxQuantity { get; set; }
    public DateTime? DepartureDate { get; set; }
    public DateTime? EtaDateTime { get; set; }
    public int FromWarehouseId { get; set; }
    public string FromWarehouseName { get; set; } = string.Empty;
    public int ToWarehouseId { get; set; }
    public string ToWarehouseName { get; set; } = string.Empty;
}
