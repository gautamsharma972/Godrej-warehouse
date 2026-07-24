namespace WarehouseGate.Domain;

public enum InwardStatus
{
    GateIn,
    Assigned,
    Docked,
    Inspecting,
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
    VehicleAtExit
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
    SuperAdmin
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
    VehicleAtGate,
    VehicleAtExit,
    LoadGroupConfirmation
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
