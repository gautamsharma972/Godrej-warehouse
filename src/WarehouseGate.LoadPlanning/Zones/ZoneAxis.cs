namespace WarehouseGate.LoadPlanning.Zones;

// LoadPlanning-local mirrors of the Domain zone enums (WarehouseGate.Domain has
// no reference to this project and vice versa, so these are duplicated - the
// Api layer maps between them). See VehicleZoneGrid for the axis convention.
public enum ZoneLength
{
    Front,
    Middle,
    Back
}

public enum ZoneWidth
{
    Left,
    Center,
    Right
}

public enum ZoneHeight
{
    Bottom,
    Middle,
    Top
}
