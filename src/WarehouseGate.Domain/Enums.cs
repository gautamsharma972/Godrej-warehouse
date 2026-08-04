namespace WarehouseGate.Domain;

public enum InwardStatus
{
    // Security's Gate Check-in creates the job directly at this status, whether or not it's linked
    // to a Dispatch Plan PO yet (see InwardService.CheckInAsync/LinkVehicleAsync) - a job can sit
    // here with PurchaseOrderId == null until Office links it from the Expected tab.
    GateIn,
    Assigned,
    Docked,
    Inspecting,
    // Unloading is done and inspection was recorded, but Office hasn't reviewed/confirmed it yet -
    // no GRN exists until VerifyAndGenerateGrnAsync runs (see InwardService).
    PendingOfficeVerification,
    Completed
}

public enum PhotoType
{
    VehicleBefore,
    MaterialBefore,
    ExceptionProof,
    VehicleAtGate,
    VehicleFront,
    VehicleRear,
    VehicleLeft,
    VehicleRight,
    VehicleSeal,
    VehicleDamage,
    VehicleAtExit,
    // Per-SKU Receiving Inspection photo (up to 2 per PO line) - see PhotoEvidence.PurchaseOrderLineId.
    SkuCondition
}

public enum DocumentType
{
    EWayBill,
    Invoice,
    LorryReceipt,
    Other
}

public enum MaterialCondition
{
    Ok,
    Damaged,
    Short,
    Excess,
    Mismatch
}

public enum UserRole
{
    Security,
    Supervisor,
    Office,
    LogisticsManager,
    SuperAdmin,
    // Manages Organizations from the platform site - not scoped to any single organization
    // (ApplicationUser.OrganizationId is null for this role). See PlatformController.
    PlatformAdmin
}

public enum WarehouseType
{
    Warehouse,
    CPA
}

public enum AuditAction
{
    Created,
    Updated,
    Deleted
}

public enum OutwardStatus
{
    PickListGenerated,
    Assigned,
    Docked,
    Loading,
    // Supervisor has finished loading and tapped "Complete Loading", but Office hasn't reviewed/
    // confirmed it yet - no OutwardDispatchNote exists until VerifyAndCompleteAsync runs (see
    // OutwardService). Mirrors InwardStatus.PendingOfficeVerification.
    PendingOfficeVerification,
    Completed
}

// Independent of InwardStatus/OutwardStatus - a Dispatch Plan record is a Logistics Manager's
// own advance-planning row (from Excel or manual entry) that a matching Inward/Outward job
// (same vehicle number + warehouse) may or may not ever materialize against. InTransit is the
// default from creation; Completed is set automatically once that matching job finishes its
// Supervisor process. Inactive/Deleted are manager-driven only.
public enum VehicleLogisticsStatus
{
    InTransit,
    Inactive,
    Deleted,
    Completed,
    // Claimed by a real Inward/Outward job (gate check-in or pick-list generation) but that job
    // hasn't finished yet - see VehicleLogisticsRecord.ConsumedByInwardTransactionId/
    // ConsumedByOutwardTransactionId for the link to which job.
    InProgress
}

public enum OutwardPhotoType
{
    VehicleLoaded,
    MaterialLoaded,
    ExceptionProof,
    Seal,
    // Gate-arrival evidence (see OutwardGateArrivalPhoto) - VehicleAtGate now allows up to 5 photos
    // instead of 1; Driver/VehicleRc/DrivingLicense are capped at 3/2/2 respectively (see
    // OutwardService.AddGateArrivalPhotoAsync).
    VehicleAtGate,
    Driver,
    VehicleRc,
    DrivingLicense,
    VehicleAtExit,
    LoadGroupConfirmation,
    // Per-SKU Load Confirmation evidence (see OutwardPhotoEvidence.DispatchOrderLineId) - capped at
    // 2 per line and 10 per job overall (OutwardService.AddPhotoAsync). Mirrors PhotoType.SkuCondition.
    SkuLoaded
}

public enum OutwardExceptionReason
{
    PartialLoad,
    StockNotAvailable,
    VehicleCapacityExceeded,
    MaterialDamaged,
    WrongMaterialLoaded
}

// Zone-grid axes for the 3D load plan - see OutwardLoadPlanGroup for the full
// convention. Front/Back = length (Z), Left/Right = width (X), Bottom/Top = height (Y).
public enum LoadZoneLength
{
    Front,
    Middle,
    Back
}

public enum LoadZoneWidth
{
    Left,
    Right
}

public enum LoadZoneHeight
{
    Bottom,
    Middle,
    Top
}

// Domain-local mirror of WarehouseGate.LoadPlanning.Models.Orientation - kept
// separate because Domain has no project reference to LoadPlanning; the Api
// layer maps between them.
public enum LoadOrientation
{
    LWH,
    WLH,
    LHW,
    HLW,
    WHL,
    HWL
}

public enum LoadGroupConfirmationStatus
{
    NotStarted,
    Started,
    Loaded,
    Mismatch,
    ShortLoad,
    Skipped
}

// SKU master weight class, used by load-plan validation to flag heavy-over-light
// and fragile-item stacking without depending on exact per-carton weight ratios.
public enum WeightCategory
{
    Fragile,
    Light,
    Medium,
    Heavy
}
