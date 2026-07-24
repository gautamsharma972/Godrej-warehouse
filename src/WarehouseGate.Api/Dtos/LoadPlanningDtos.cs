namespace WarehouseGate.Api.Dtos;

public record VehicleProfileDto(string Name, double Length, double Width, double Height, double MaxPayload);

public record PlacedItemDto(
    string Sku,
    string Description,
    double X,
    double Y,
    double Z,
    double Length,
    double Width,
    double Height,
    string Rotation,
    string Color,
    int StackLevel,
    double SupportArea,
    double Weight,
    int LoadSequence,
    List<string> ViolationReasons);

public record LoadSimulationDto(
    double VehicleUtilizationPct,
    double WeightUtilizationPct,
    double CenterOfGravityX,
    double CenterOfGravityY,
    double CenterOfGravityZ,
    double RemainingCapacityKg,
    double RemainingVolumeM3,
    int RuleViolationCount,
    int UnplacedCount,
    double OptimizationScore,
    string BalanceStatus);

public record LoadPlanResultDto(
    VehicleProfileDto Vehicle,
    List<PlacedItemDto> Items,
    List<string> UnplacedSkus,
    LoadSimulationDto Simulation);

public record LoadPlanItemOverrideDto(string Sku, int Qty);

public record OptimizeLoadPlanRequest(string VehicleProfileName, List<LoadPlanItemOverrideDto>? Items);
