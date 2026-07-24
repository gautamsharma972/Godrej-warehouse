namespace WarehouseGate.Domain;

// One placed SKU batch ("box group") within a saved OutwardLoadPlanOption - the
// placement unit the supervisor works with (never an individual carton). Both
// the zone (the supervisor's intent - drives step labels/Top View grid) and the
// exact X/Y/Z geometry (what's actually rendered and rule-checked) are stored;
// they're set together at placement time and never diverge in normal use.
//
// Zone axis convention (new - not derived from anything existing elsewhere):
// Length (Z): Front = near Z=0 (the wall opposite the doors - "loaded first" in
// the LoadPlanning engine's existing convention), Back = near the doors.
// Width (X): Left/Right as viewed standing at the rear doors looking toward the
// front of the vehicle. Height (Y): Bottom = floor, Top = ceiling.
public class OutwardLoadPlanGroup
{
    public int Id { get; set; }

    public int OutwardLoadPlanOptionId { get; set; }
    public OutwardLoadPlanOption? OutwardLoadPlanOption { get; set; }

    // Multiple groups may reference the same line (a batch split across zones/quantities).
    public int DispatchOrderLineId { get; set; }
    public DispatchOrderLine? DispatchOrderLine { get; set; }

    public int Quantity { get; set; }

    public LoadZoneLength ZoneLength { get; set; }
    public LoadZoneWidth ZoneWidth { get; set; }
    public LoadZoneHeight ZoneHeight { get; set; }

    public decimal PositionXCm { get; set; }
    public decimal PositionYCm { get; set; }
    public decimal PositionZCm { get; set; }

    public decimal DimXCm { get; set; }
    public decimal DimYCm { get; set; }
    public decimal DimZCm { get; set; }

    public LoadOrientation Orientation { get; set; }

    // Resolved concrete grid even when the supervisor picked "Auto", so the
    // placement is reproducible on reload instead of being re-randomized.
    public int Rows { get; set; }
    public int Columns { get; set; }
    public int Layers { get; set; }

    public int LoadSequence { get; set; }
    public string Color { get; set; } = string.Empty;
    public bool IsLocked { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;

    // Actual Loading Confirmation fields - all left at defaults until Confirm
    // Loading Mode touches this group.
    public LoadGroupConfirmationStatus ConfirmationStatus { get; set; } = LoadGroupConfirmationStatus.NotStarted;
    public int? ActualQuantity { get; set; }
    public string? ActualNotes { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedByUserId { get; set; }

    public List<OutwardPhotoEvidence> Photos { get; set; } = new();
}
