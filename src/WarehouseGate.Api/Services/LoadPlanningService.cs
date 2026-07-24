using WarehouseGate.Api.Dtos;
using WarehouseGate.LoadPlanning;

namespace WarehouseGate.Api.Services;

public class LoadPlanningService
{
    private readonly LoadPlanningEngine _engine;

    public LoadPlanningService(LoadPlanningEngine engine)
    {
        _engine = engine;
    }

    public Task<List<VehicleProfileDto>> GetVehicleProfilesAsync() =>
        Task.FromResult(_engine.VehicleProfiles.Select(LoadPlanningResultMapper.ToDto).ToList());

    public Task<LoadPlanResultDto> OptimizeAsync(OptimizeLoadPlanRequest request)
    {
        var overrides = request.Items?.Select(i => (i.Sku, i.Qty)).ToList();
        var result = _engine.Optimize(request.VehicleProfileName, overrides);
        return Task.FromResult(LoadPlanningResultMapper.ToDto(result));
    }
}
