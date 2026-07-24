namespace WarehouseGate.LoadPlanning.Models;

public enum FragilityLevel
{
    None,
    Low,
    Medium,
    High
}

public enum Stackability
{
    NotStackable,
    StackableSameSku,
    StackableAny
}

public enum OrientationRestriction
{
    None,
    UprightOnly
}

public enum HazardClass
{
    None,
    Flammable,
    Corrosive,
    Toxic
}

public enum TemperatureClass
{
    Ambient,
    Chilled,
    Frozen
}

public enum PackagingType
{
    Carton,
    Crate,
    Pallet,
    Loose,
    Drum
}

public enum DoorPosition
{
    Rear,
    Side
}

public enum LoadingType
{
    Manual,
    Forklift,
    Conveyor
}

// The 6 axis-aligned rotations of a cuboid (which physical dimension maps to
// which vehicle axis). Only a subset is valid for a given item depending on
// RotationAllowed/OrientationRestriction.
public enum Orientation
{
    LWH,
    WLH,
    LHW,
    HLW,
    WHL,
    HWL
}
