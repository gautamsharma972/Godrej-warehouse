using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.DummyData;

// Standalone dummy master data (dimensions in cm, weight in kg) - deliberately not
// wired to the real Product/Vehicle EF entities, which only carry a handful of
// fields. This is Phase 1 scaffolding until real master data grows to match.
public static class DummyCatalog
{
    public static IReadOnlyList<ProductItem> Products { get; } = new List<ProductItem>
    {
        new()
        {
            Sku = "STL-ALM", Description = "Steel Almirah", Quantity = 2,
            Length = 90, Width = 45, Height = 185, Weight = 45,
            FragilityLevel = FragilityLevel.None, Stackability = Stackability.NotStackable,
            MaxStackWeight = 0, MaxStackHeight = 0, OrientationRestriction = OrientationRestriction.UprightOnly,
            LoadingPriority = 10, PackagingType = PackagingType.Crate, DeliverySequence = 1
        },
        new()
        {
            Sku = "MS-SHEET-2MM", Description = "MS Sheet 2mm", Quantity = 15,
            Length = 100, Width = 100, Height = 0.2, Weight = 8,
            Stackability = Stackability.StackableAny, MaxStackHeight = 20, MaxStackWeight = 200,
            LoadingPriority = 20, PackagingType = PackagingType.Loose, DeliverySequence = 2
        },
        new()
        {
            Sku = "CEIL-FAN-48", Description = "Ceiling Fan 48in", Quantity = 6,
            Length = 60, Width = 60, Height = 30, Weight = 5.5,
            FragilityLevel = FragilityLevel.Medium, Stackability = Stackability.StackableSameSku,
            MaxStackWeight = 20, MaxStackHeight = 90, LoadingPriority = 40, PackagingType = PackagingType.Carton, DeliverySequence = 1
        },
        new()
        {
            Sku = "LED-BULB-9W-CTN", Description = "LED Bulb 9W Carton", Quantity = 10,
            Length = 30, Width = 30, Height = 30, Weight = 8,
            FragilityLevel = FragilityLevel.High, Stackability = Stackability.StackableSameSku,
            MaxStackWeight = 16, MaxStackHeight = 60, CrushStrength = 25,
            LoadingPriority = 70, PackagingType = PackagingType.Carton, DeliverySequence = 3
        },
        new()
        {
            Sku = "PVC-PIPE-2IN", Description = "PVC Pipe 2in Bundle", Quantity = 4,
            Length = 300, Width = 15, Height = 15, Weight = 22,
            Stackability = Stackability.StackableAny, MaxStackWeight = 60, MaxStackHeight = 60,
            LoadingPriority = 15, PackagingType = PackagingType.Loose, DeliverySequence = 2
        },
        new()
        {
            Sku = "HEX-BOLT-M12-CTN", Description = "Hex Bolt M12 Carton", Quantity = 8,
            Length = 40, Width = 30, Height = 20, Weight = 15,
            Stackability = Stackability.StackableAny, MaxStackWeight = 60, MaxStackHeight = 60,
            LoadingPriority = 50, PackagingType = PackagingType.Carton, DeliverySequence = 2
        },
        new()
        {
            Sku = "PAINT-DRUM-20L", Description = "Paint Drum 20L", Quantity = 6,
            Length = 40, Width = 40, Height = 55, Weight = 25,
            HazardClass = HazardClass.Flammable, OrientationRestriction = OrientationRestriction.UprightOnly,
            Stackability = Stackability.NotStackable, LoadingPriority = 25, PackagingType = PackagingType.Drum, DeliverySequence = 1
        },
        new()
        {
            Sku = "GLASS-PANEL-5MM", Description = "Glass Panel 5mm", Quantity = 5,
            Length = 120, Width = 80, Height = 0.5, Weight = 12,
            FragilityLevel = FragilityLevel.High, Stackability = Stackability.StackableSameSku,
            MaxStackWeight = 24, MaxStackHeight = 6, CrushStrength = 10,
            RotationAllowed = false, LoadingPriority = 80, PackagingType = PackagingType.Crate, DeliverySequence = 3
        },
        new()
        {
            Sku = "FROZEN-SNACK-CTN", Description = "Frozen Snacks Carton", Quantity = 10,
            Length = 40, Width = 30, Height = 25, Weight = 10,
            TemperatureClass = TemperatureClass.Frozen, Stackability = Stackability.StackableAny,
            MaxStackWeight = 40, MaxStackHeight = 75, LoadingPriority = 60, PackagingType = PackagingType.Carton, DeliverySequence = 1
        },
        new()
        {
            Sku = "EXT-CORD-5M", Description = "Extension Cord 5m", Quantity = 20,
            Length = 20, Width = 15, Height = 8, Weight = 0.6,
            Stackability = Stackability.StackableAny, MaxStackWeight = 15, MaxStackHeight = 40,
            LoadingPriority = 90, PackagingType = PackagingType.Carton, DeliverySequence = 3
        },
        new()
        {
            Sku = "CORR-BOX-LG", Description = "Corrugated Box - Large", Quantity = 8,
            Length = 60, Width = 40, Height = 40, Weight = 8,
            Stackability = Stackability.StackableAny, MaxStackWeight = 40, MaxStackHeight = 120,
            LoadingPriority = 55, PackagingType = PackagingType.Carton, DeliverySequence = 2
        },
        new()
        {
            Sku = "PIPE-ELBOW-2IN-CTN", Description = "Pipe Elbow 2in Carton", Quantity = 12,
            Length = 20, Width = 20, Height = 20, Weight = 5,
            Stackability = Stackability.StackableAny, MaxStackWeight = 20, MaxStackHeight = 60,
            LoadingPriority = 65, PackagingType = PackagingType.Carton, DeliverySequence = 3
        }
    };

    public static IReadOnlyList<VehicleProfile> VehicleProfiles { get; } = new List<VehicleProfile>
    {
        // Axle limits must comfortably exceed EmptyWeight/2 (the baseline each axle
        // already carries with no cargo at all) plus most of MaxPayload - otherwise
        // AxleRule would reject every single item before any cargo is even placed.
        new()
        {
            Name = "SmallTruck", Length = 300, Width = 160, Height = 160,
            MaxPayload = 1500, FrontAxleLimit = 2200, RearAxleLimit = 2350, EmptyWeight = 2200,
            DoorPosition = DoorPosition.Rear, LoadingType = LoadingType.Manual
        },
        new()
        {
            Name = "MediumTruck", Length = 430, Width = 180, Height = 180,
            MaxPayload = 2500, FrontAxleLimit = 3450, RearAxleLimit = 3700, EmptyWeight = 3200,
            DoorPosition = DoorPosition.Rear, LoadingType = LoadingType.Forklift
        },
        new()
        {
            Name = "LargeTruck", Length = 480, Width = 200, Height = 190,
            MaxPayload = 3000, FrontAxleLimit = 4200, RearAxleLimit = 4500, EmptyWeight = 4000,
            DoorPosition = DoorPosition.Rear, LoadingType = LoadingType.Forklift, SideLoadingSupport = true
        },
        new()
        {
            Name = "RefrigeratedTruck", Length = 400, Width = 180, Height = 180,
            MaxPayload = 2200, FrontAxleLimit = 3400, RearAxleLimit = 3650, EmptyWeight = 3600,
            DoorPosition = DoorPosition.Rear, LoadingType = LoadingType.Forklift,
            SupportsChilled = true, SupportsFrozen = true
        }
    };

    public static VehicleProfile? FindVehicleProfile(string name) =>
        VehicleProfiles.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
}
