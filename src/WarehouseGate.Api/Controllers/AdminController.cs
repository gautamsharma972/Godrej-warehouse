using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Hubs;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminController : ControllerBase
{
    private readonly WarehouseGateDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AuditService _audit;
    private readonly IHubContext<InwardHub> _hub;
    private readonly ICurrentTenantProvider _tenant;

    public AdminController(WarehouseGateDbContext db, UserManager<ApplicationUser> userManager, AuditService audit, IHubContext<InwardHub> hub, ICurrentTenantProvider tenant)
    {
        _db = db;
        _userManager = userManager;
        _audit = audit;
        _hub = hub;
        _tenant = tenant;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string CurrentUserName => User.FindFirstValue("displayName") ?? User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

    // Organizations isn't itself tenant-scoped, so this is the one place AdminController needs
    // to reach outside the automatic per-request query filter - just to read its own org's limits.
    private Task<Organization> GetCurrentOrganizationAsync() =>
        _db.Organizations.FirstAsync(o => o.Id == _tenant.OrganizationId);

    // ============================ COUNTRIES ============================

    [HttpGet("countries")]
    public async Task<ActionResult<List<CountryDto>>> GetCountries() =>
        Ok(await _db.Countries.OrderBy(c => c.Name).Select(c => new CountryDto(c.Id, c.Name)).ToListAsync());

    [HttpPost("countries")]
    public async Task<ActionResult<CountryDto>> CreateCountry(UpsertCountryRequest request)
    {
        var country = new Country { Name = request.Name.Trim() };
        _db.Countries.Add(country);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Country", country.Id, AuditAction.Created, $"Country '{country.Name}' created.", CurrentUserId, CurrentUserName);
        return Ok(new CountryDto(country.Id, country.Name));
    }

    [HttpPut("countries/{id:int}")]
    public async Task<ActionResult<CountryDto>> UpdateCountry(int id, UpsertCountryRequest request)
    {
        var country = await _db.Countries.FindAsync(id);
        if (country is null)
        {
            return NotFound();
        }

        country.Name = request.Name.Trim();
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Country", country.Id, AuditAction.Updated, $"Country '{country.Name}' updated.", CurrentUserId, CurrentUserName);
        return Ok(new CountryDto(country.Id, country.Name));
    }

    [HttpDelete("countries/{id:int}")]
    public async Task<IActionResult> DeleteCountry(int id)
    {
        var country = await _db.Countries.FindAsync(id);
        if (country is null)
        {
            return NotFound();
        }

        _db.Countries.Remove(country);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Country", id, AuditAction.Deleted, $"Country '{country.Name}' deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    // ============================ STATES ============================

    [HttpGet("states")]
    public async Task<ActionResult<List<StateDto>>> GetStates([FromQuery] int? countryId)
    {
        var query = _db.States.Include(s => s.Country).AsQueryable();
        if (countryId is not null)
        {
            query = query.Where(s => s.CountryId == countryId);
        }

        var states = await query.OrderBy(s => s.Name).ToListAsync();
        return Ok(states.Select(s => new StateDto(s.Id, s.Name, s.CountryId, s.Country!.Name)).ToList());
    }

    [HttpPost("states")]
    public async Task<ActionResult<StateDto>> CreateState(UpsertStateRequest request)
    {
        var country = await _db.Countries.FindAsync(request.CountryId);
        if (country is null)
        {
            return BadRequest(new { message = "Country not found." });
        }

        var state = new State { Name = request.Name.Trim(), CountryId = request.CountryId };
        _db.States.Add(state);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("State", state.Id, AuditAction.Created, $"State '{state.Name}' created.", CurrentUserId, CurrentUserName);
        return Ok(new StateDto(state.Id, state.Name, state.CountryId, country.Name));
    }

    [HttpPut("states/{id:int}")]
    public async Task<ActionResult<StateDto>> UpdateState(int id, UpsertStateRequest request)
    {
        var state = await _db.States.FindAsync(id);
        if (state is null)
        {
            return NotFound();
        }

        var country = await _db.Countries.FindAsync(request.CountryId);
        if (country is null)
        {
            return BadRequest(new { message = "Country not found." });
        }

        state.Name = request.Name.Trim();
        state.CountryId = request.CountryId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("State", state.Id, AuditAction.Updated, $"State '{state.Name}' updated.", CurrentUserId, CurrentUserName);
        return Ok(new StateDto(state.Id, state.Name, state.CountryId, country.Name));
    }

    [HttpDelete("states/{id:int}")]
    public async Task<IActionResult> DeleteState(int id)
    {
        var state = await _db.States.FindAsync(id);
        if (state is null)
        {
            return NotFound();
        }

        _db.States.Remove(state);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("State", id, AuditAction.Deleted, $"State '{state.Name}' deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    // ============================ CITIES ============================

    [HttpGet("cities")]
    public async Task<ActionResult<List<CityDto>>> GetCities([FromQuery] int? stateId)
    {
        var query = _db.Cities.Include(c => c.State).AsQueryable();
        if (stateId is not null)
        {
            query = query.Where(c => c.StateId == stateId);
        }

        var cities = await query.OrderBy(c => c.Name).ToListAsync();
        return Ok(cities.Select(c => new CityDto(c.Id, c.Name, c.StateId, c.State!.Name)).ToList());
    }

    [HttpPost("cities")]
    public async Task<ActionResult<CityDto>> CreateCity(UpsertCityRequest request)
    {
        var state = await _db.States.FindAsync(request.StateId);
        if (state is null)
        {
            return BadRequest(new { message = "State not found." });
        }

        var city = new City { Name = request.Name.Trim(), StateId = request.StateId };
        _db.Cities.Add(city);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("City", city.Id, AuditAction.Created, $"City '{city.Name}' created.", CurrentUserId, CurrentUserName);
        return Ok(new CityDto(city.Id, city.Name, city.StateId, state.Name));
    }

    [HttpPut("cities/{id:int}")]
    public async Task<ActionResult<CityDto>> UpdateCity(int id, UpsertCityRequest request)
    {
        var city = await _db.Cities.FindAsync(id);
        if (city is null)
        {
            return NotFound();
        }

        var state = await _db.States.FindAsync(request.StateId);
        if (state is null)
        {
            return BadRequest(new { message = "State not found." });
        }

        city.Name = request.Name.Trim();
        city.StateId = request.StateId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("City", city.Id, AuditAction.Updated, $"City '{city.Name}' updated.", CurrentUserId, CurrentUserName);
        return Ok(new CityDto(city.Id, city.Name, city.StateId, state.Name));
    }

    [HttpDelete("cities/{id:int}")]
    public async Task<IActionResult> DeleteCity(int id)
    {
        var city = await _db.Cities.FindAsync(id);
        if (city is null)
        {
            return NotFound();
        }

        _db.Cities.Remove(city);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("City", id, AuditAction.Deleted, $"City '{city.Name}' deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    // ============================ REGIONS ============================

    [HttpGet("regions")]
    public async Task<ActionResult<List<RegionDto>>> GetRegions() =>
        Ok(await _db.Regions.OrderBy(r => r.Name).Select(r => new RegionDto(r.Id, r.Name)).ToListAsync());

    [HttpPost("regions")]
    public async Task<ActionResult<RegionDto>> CreateRegion(UpsertRegionRequest request)
    {
        var region = new Region { Name = request.Name.Trim() };
        _db.Regions.Add(region);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Region", region.Id, AuditAction.Created, $"Region '{region.Name}' created.", CurrentUserId, CurrentUserName);
        return Ok(new RegionDto(region.Id, region.Name));
    }

    [HttpPut("regions/{id:int}")]
    public async Task<ActionResult<RegionDto>> UpdateRegion(int id, UpsertRegionRequest request)
    {
        var region = await _db.Regions.FindAsync(id);
        if (region is null)
        {
            return NotFound();
        }

        region.Name = request.Name.Trim();
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Region", region.Id, AuditAction.Updated, $"Region '{region.Name}' updated.", CurrentUserId, CurrentUserName);
        return Ok(new RegionDto(region.Id, region.Name));
    }

    [HttpDelete("regions/{id:int}")]
    public async Task<IActionResult> DeleteRegion(int id)
    {
        var region = await _db.Regions.FindAsync(id);
        if (region is null)
        {
            return NotFound();
        }

        _db.Regions.Remove(region);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Region", id, AuditAction.Deleted, $"Region '{region.Name}' deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    // ============================ TRANSPORTERS ============================

    [HttpGet("transporters")]
    public async Task<ActionResult<List<TransporterDto>>> GetTransporters() =>
        Ok(await _db.Transporters.OrderBy(t => t.Name)
            .Select(t => new TransporterDto(t.Id, t.Name, t.ContactPhone, t.IsActive)).ToListAsync());

    [HttpPost("transporters")]
    public async Task<ActionResult<TransporterDto>> CreateTransporter(UpsertTransporterRequest request)
    {
        var transporter = new Transporter { Name = request.Name.Trim(), ContactPhone = request.ContactPhone, IsActive = request.IsActive };
        _db.Transporters.Add(transporter);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Transporter", transporter.Id, AuditAction.Created, $"Transporter '{transporter.Name}' created.", CurrentUserId, CurrentUserName);
        return Ok(new TransporterDto(transporter.Id, transporter.Name, transporter.ContactPhone, transporter.IsActive));
    }

    [HttpPut("transporters/{id:int}")]
    public async Task<ActionResult<TransporterDto>> UpdateTransporter(int id, UpsertTransporterRequest request)
    {
        var transporter = await _db.Transporters.FindAsync(id);
        if (transporter is null)
        {
            return NotFound();
        }

        transporter.Name = request.Name.Trim();
        transporter.ContactPhone = request.ContactPhone;
        transporter.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Transporter", transporter.Id, AuditAction.Updated, $"Transporter '{transporter.Name}' updated.", CurrentUserId, CurrentUserName);
        return Ok(new TransporterDto(transporter.Id, transporter.Name, transporter.ContactPhone, transporter.IsActive));
    }

    [HttpDelete("transporters/{id:int}")]
    public async Task<IActionResult> DeleteTransporter(int id)
    {
        var transporter = await _db.Transporters.FindAsync(id);
        if (transporter is null)
        {
            return NotFound();
        }

        _db.Transporters.Remove(transporter);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Transporter", id, AuditAction.Deleted, $"Transporter '{transporter.Name}' deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    // ============================ VEHICLE TYPES ============================

    [HttpGet("vehicle-types")]
    public async Task<ActionResult<List<VehicleTypeDto>>> GetVehicleTypes() =>
        Ok(await _db.VehicleTypes.OrderBy(t => t.Name).Select(t => new VehicleTypeDto(t.Id, t.Name)).ToListAsync());

    [HttpPost("vehicle-types")]
    public async Task<ActionResult<VehicleTypeDto>> CreateVehicleType(UpsertVehicleTypeRequest request)
    {
        var type = new VehicleType { Name = request.Name.Trim() };
        _db.VehicleTypes.Add(type);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleType", type.Id, AuditAction.Created, $"Vehicle type '{type.Name}' created.", CurrentUserId, CurrentUserName);
        return Ok(new VehicleTypeDto(type.Id, type.Name));
    }

    [HttpPut("vehicle-types/{id:int}")]
    public async Task<ActionResult<VehicleTypeDto>> UpdateVehicleType(int id, UpsertVehicleTypeRequest request)
    {
        var type = await _db.VehicleTypes.FindAsync(id);
        if (type is null)
        {
            return NotFound();
        }

        type.Name = request.Name.Trim();
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleType", type.Id, AuditAction.Updated, $"Vehicle type '{type.Name}' updated.", CurrentUserId, CurrentUserName);
        return Ok(new VehicleTypeDto(type.Id, type.Name));
    }

    [HttpDelete("vehicle-types/{id:int}")]
    public async Task<IActionResult> DeleteVehicleType(int id)
    {
        var type = await _db.VehicleTypes.FindAsync(id);
        if (type is null)
        {
            return NotFound();
        }

        _db.VehicleTypes.Remove(type);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleType", id, AuditAction.Deleted, $"Vehicle type '{type.Name}' deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    // ============================ VEHICLE CATEGORIES ============================

    [HttpGet("vehicle-categories")]
    public async Task<ActionResult<List<VehicleCategoryDto>>> GetVehicleCategories() =>
        Ok(await _db.VehicleCategories.OrderBy(c => c.Name).Select(c => new VehicleCategoryDto(c.Id, c.Name)).ToListAsync());

    [HttpPost("vehicle-categories")]
    public async Task<ActionResult<VehicleCategoryDto>> CreateVehicleCategory(UpsertVehicleCategoryRequest request)
    {
        var category = new VehicleCategory { Name = request.Name.Trim() };
        _db.VehicleCategories.Add(category);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleCategory", category.Id, AuditAction.Created, $"Vehicle category '{category.Name}' created.", CurrentUserId, CurrentUserName);
        return Ok(new VehicleCategoryDto(category.Id, category.Name));
    }

    [HttpPut("vehicle-categories/{id:int}")]
    public async Task<ActionResult<VehicleCategoryDto>> UpdateVehicleCategory(int id, UpsertVehicleCategoryRequest request)
    {
        var category = await _db.VehicleCategories.FindAsync(id);
        if (category is null)
        {
            return NotFound();
        }

        category.Name = request.Name.Trim();
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleCategory", category.Id, AuditAction.Updated, $"Vehicle category '{category.Name}' updated.", CurrentUserId, CurrentUserName);
        return Ok(new VehicleCategoryDto(category.Id, category.Name));
    }

    [HttpDelete("vehicle-categories/{id:int}")]
    public async Task<IActionResult> DeleteVehicleCategory(int id)
    {
        var category = await _db.VehicleCategories.FindAsync(id);
        if (category is null)
        {
            return NotFound();
        }

        _db.VehicleCategories.Remove(category);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleCategory", id, AuditAction.Deleted, $"Vehicle category '{category.Name}' deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    // ============================ VEHICLE MASTERS ============================

    [HttpGet("vehicle-masters")]
    public async Task<ActionResult<List<VehicleMasterFullDto>>> GetVehicleMasters()
    {
        var masters = await _db.VehicleMasters
            .Include(v => v.VehicleType)
            .Include(v => v.VehicleCategory)
            .OrderBy(v => v.VehicleType!.Name)
            .ThenBy(v => v.VehicleCategory!.Name)
            .ToListAsync();

        return Ok(masters.Select(ToVehicleMasterDto).ToList());
    }

    [HttpPost("vehicle-masters")]
    public async Task<ActionResult<VehicleMasterFullDto>> CreateVehicleMaster(UpsertVehicleMasterRequest request)
    {
        var validation = await ValidateVehicleProfileAsync(request);
        if (validation is not null)
        {
            return validation;
        }

        var master = new VehicleMaster
        {
            VehicleTypeId = request.VehicleTypeId,
            VehicleCategoryId = request.VehicleCategoryId,
            MaxWeightKg = request.MaxWeightKg,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm
        };
        _db.VehicleMasters.Add(master);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleMaster", master.Id, AuditAction.Created, "Vehicle master profile created.", CurrentUserId, CurrentUserName);

        return await GetVehicleMasterDtoAsync(master.Id);
    }

    [HttpPut("vehicle-masters/{id:int}")]
    public async Task<ActionResult<VehicleMasterFullDto>> UpdateVehicleMaster(int id, UpsertVehicleMasterRequest request)
    {
        var master = await _db.VehicleMasters.FindAsync(id);
        if (master is null)
        {
            return NotFound();
        }

        var validation = await ValidateVehicleProfileAsync(request);
        if (validation is not null)
        {
            return validation;
        }

        master.VehicleTypeId = request.VehicleTypeId;
        master.VehicleCategoryId = request.VehicleCategoryId;
        master.MaxWeightKg = request.MaxWeightKg;
        master.LengthCm = request.LengthCm;
        master.WidthCm = request.WidthCm;
        master.HeightCm = request.HeightCm;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleMaster", master.Id, AuditAction.Updated, "Vehicle master profile updated.", CurrentUserId, CurrentUserName);

        return await GetVehicleMasterDtoAsync(master.Id);
    }

    [HttpDelete("vehicle-masters/{id:int}")]
    public async Task<IActionResult> DeleteVehicleMaster(int id)
    {
        var master = await _db.VehicleMasters.FindAsync(id);
        if (master is null)
        {
            return NotFound();
        }

        _db.VehicleMasters.Remove(master);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleMaster", id, AuditAction.Deleted, "Vehicle master profile deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    private async Task<ActionResult<VehicleMasterFullDto>> GetVehicleMasterDtoAsync(int id)
    {
        var master = await _db.VehicleMasters
            .Include(v => v.VehicleType)
            .Include(v => v.VehicleCategory)
            .FirstAsync(v => v.Id == id);
        return Ok(ToVehicleMasterDto(master));
    }

    private async Task<ActionResult<VehicleMasterFullDto>?> ValidateVehicleProfileAsync(UpsertVehicleMasterRequest request)
    {
        if (!await _db.VehicleTypes.AnyAsync(t => t.Id == request.VehicleTypeId))
        {
            return BadRequest(new { message = "Vehicle type not found." });
        }

        if (!await _db.VehicleCategories.AnyAsync(c => c.Id == request.VehicleCategoryId))
        {
            return BadRequest(new { message = "Vehicle category not found." });
        }

        return null;
    }

    private static VehicleMasterFullDto ToVehicleMasterDto(VehicleMaster v) => new(
        v.Id, v.VehicleTypeId, v.VehicleType!.Name, v.VehicleCategoryId, v.VehicleCategory!.Name,
        v.MaxWeightKg, v.LengthCm, v.WidthCm, v.HeightCm);

    // ============================ VEHICLES (REGISTRY) ============================
    // Individual plates created lazily at Gate-In/Dock-In - also backfilled with Vehicle's own
    // default capacity there now (see InwardService.CheckInAsync/OutwardService.
    // BackfillVehicleCapacity), so this screen mainly lets SuperAdmin fix a specific plate's
    // capacity to something more accurate than the generic default.

    [HttpGet("vehicles")]
    public async Task<ActionResult<List<VehicleDto>>> GetVehicles() =>
        Ok(await _db.Vehicles.OrderBy(v => v.Number).Select(v => ToVehicleDto(v)).ToListAsync());

    [HttpPost("vehicles")]
    public async Task<ActionResult<VehicleDto>> CreateVehicle(UpsertVehicleRequest request)
    {
        var number = request.Number.Trim();
        if (number.Length == 0)
        {
            return BadRequest(new { message = "Vehicle number is required." });
        }

        if (await _db.Vehicles.AnyAsync(v => v.Number == number))
        {
            return BadRequest(new { message = "A vehicle with this number already exists." });
        }

        var vehicle = new Vehicle
        {
            Number = number,
            MaxWeightKg = request.MaxWeightKg ?? Vehicle.DefaultMaxWeightKg,
            LengthCm = request.LengthCm ?? Vehicle.DefaultLengthCm,
            WidthCm = request.WidthCm ?? Vehicle.DefaultWidthCm,
            HeightCm = request.HeightCm ?? Vehicle.DefaultHeightCm
        };
        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Vehicle", vehicle.Id, AuditAction.Created, $"Vehicle '{vehicle.Number}' registered.", CurrentUserId, CurrentUserName);

        return Ok(ToVehicleDto(vehicle));
    }

    [HttpPut("vehicles/{id:int}")]
    public async Task<ActionResult<VehicleDto>> UpdateVehicle(int id, UpsertVehicleRequest request)
    {
        var vehicle = await _db.Vehicles.FindAsync(id);
        if (vehicle is null)
        {
            return NotFound();
        }

        var number = request.Number.Trim();
        if (number.Length == 0)
        {
            return BadRequest(new { message = "Vehicle number is required." });
        }

        if (await _db.Vehicles.AnyAsync(v => v.Number == number && v.Id != id))
        {
            return BadRequest(new { message = "A vehicle with this number already exists." });
        }

        vehicle.Number = number;
        vehicle.MaxWeightKg = request.MaxWeightKg ?? Vehicle.DefaultMaxWeightKg;
        vehicle.LengthCm = request.LengthCm ?? Vehicle.DefaultLengthCm;
        vehicle.WidthCm = request.WidthCm ?? Vehicle.DefaultWidthCm;
        vehicle.HeightCm = request.HeightCm ?? Vehicle.DefaultHeightCm;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Vehicle", vehicle.Id, AuditAction.Updated, $"Vehicle '{vehicle.Number}' capacity updated.", CurrentUserId, CurrentUserName);

        return Ok(ToVehicleDto(vehicle));
    }

    private static VehicleDto ToVehicleDto(Vehicle v) => new(v.Id, v.Number, v.MaxWeightKg, v.LengthCm, v.WidthCm, v.HeightCm);

    // ============================ PRODUCTS (SKU MASTER) ============================

    [HttpGet("products")]
    public async Task<ActionResult<List<ProductDto>>> GetProducts() =>
        Ok(await _db.Products.OrderBy(p => p.Name).Select(p => ToProductDto(p)).ToListAsync());

    [HttpPost("products")]
    public async Task<ActionResult<ProductDto>> CreateProduct(UpsertProductRequest request)
    {
        if (!Enum.TryParse<WeightCategory>(request.Category, out var category))
        {
            return BadRequest($"Unknown weight category '{request.Category}'.");
        }

        var product = new Product
        {
            Name = request.Name.Trim(),
            SkuCode = request.SkuCode.Trim(),
            WeightKg = request.WeightKg,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm,
            Category = category,
            IsStackable = request.IsStackable,
            MaxStackLayers = request.MaxStackLayers,
            ColorHex = string.IsNullOrWhiteSpace(request.ColorHex) ? null : request.ColorHex.Trim()
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Product", product.Id, AuditAction.Created, $"Product '{product.Name}' created.", CurrentUserId, CurrentUserName);
        return Ok(ToProductDto(product));
    }

    [HttpPut("products/{id:int}")]
    public async Task<ActionResult<ProductDto>> UpdateProduct(int id, UpsertProductRequest request)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        if (!Enum.TryParse<WeightCategory>(request.Category, out var category))
        {
            return BadRequest($"Unknown weight category '{request.Category}'.");
        }

        product.Name = request.Name.Trim();
        product.SkuCode = request.SkuCode.Trim();
        product.WeightKg = request.WeightKg;
        product.LengthCm = request.LengthCm;
        product.WidthCm = request.WidthCm;
        product.HeightCm = request.HeightCm;
        product.Category = category;
        product.IsStackable = request.IsStackable;
        product.MaxStackLayers = request.MaxStackLayers;
        product.ColorHex = string.IsNullOrWhiteSpace(request.ColorHex) ? null : request.ColorHex.Trim();
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Product", product.Id, AuditAction.Updated, $"Product '{product.Name}' updated.", CurrentUserId, CurrentUserName);
        return Ok(ToProductDto(product));
    }

    [HttpDelete("products/{id:int}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Product", id, AuditAction.Deleted, $"Product '{product.Name}' deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    private static ProductDto ToProductDto(Product p) => new(
        p.Id, p.Name, p.SkuCode, p.WeightKg, p.LengthCm, p.WidthCm, p.HeightCm,
        p.Category.ToString(), p.IsStackable, p.MaxStackLayers, p.ColorHex);

    // ============================ LOCATIONS ============================

    [HttpGet("locations")]
    public async Task<ActionResult<List<LocationDto>>> GetLocations()
    {
        var locations = await _db.Locations
            .Include(l => l.Region)
            .Include(l => l.State)
            .Include(l => l.City)
            .OrderBy(l => l.Name)
            .ToListAsync();

        return Ok(locations.Select(ToLocationDto).ToList());
    }

    [HttpPost("locations")]
    public async Task<ActionResult<LocationDto>> CreateLocation(UpsertLocationRequest request)
    {
        var validation = await ValidateLocationAsync(request);
        if (validation is not null)
        {
            return validation;
        }

        var location = new Location
        {
            Name = request.Name.Trim(),
            RegionId = request.RegionId,
            StateId = request.StateId,
            CityId = request.CityId
        };

        _db.Locations.Add(location);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Location", location.Id, AuditAction.Created, $"Location '{location.Name}' created.", CurrentUserId, CurrentUserName);

        return await GetLocationDtoAsync(location.Id);
    }

    [HttpPut("locations/{id:int}")]
    public async Task<ActionResult<LocationDto>> UpdateLocation(int id, UpsertLocationRequest request)
    {
        var location = await _db.Locations.FindAsync(id);
        if (location is null)
        {
            return NotFound();
        }

        var validation = await ValidateLocationAsync(request);
        if (validation is not null)
        {
            return validation;
        }

        location.Name = request.Name.Trim();
        location.RegionId = request.RegionId;
        location.StateId = request.StateId;
        location.CityId = request.CityId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Location", location.Id, AuditAction.Updated, $"Location '{location.Name}' updated.", CurrentUserId, CurrentUserName);

        return await GetLocationDtoAsync(location.Id);
    }

    [HttpDelete("locations/{id:int}")]
    public async Task<IActionResult> DeleteLocation(int id)
    {
        var location = await _db.Locations.FindAsync(id);
        if (location is null)
        {
            return NotFound();
        }

        _db.Locations.Remove(location);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Location", id, AuditAction.Deleted, $"Location '{location.Name}' deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    private async Task<ActionResult<LocationDto>> GetLocationDtoAsync(int id)
    {
        var location = await _db.Locations
            .Include(l => l.Region)
            .Include(l => l.State)
            .Include(l => l.City)
            .FirstAsync(l => l.Id == id);
        return Ok(ToLocationDto(location));
    }

    private async Task<ActionResult<LocationDto>?> ValidateLocationAsync(UpsertLocationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Location name is required." });
        }

        if (!await _db.Regions.AnyAsync(r => r.Id == request.RegionId))
        {
            return BadRequest(new { message = "Region not found." });
        }

        if (!await _db.States.AnyAsync(s => s.Id == request.StateId))
        {
            return BadRequest(new { message = "State not found." });
        }

        if (!await _db.Cities.AnyAsync(c => c.Id == request.CityId && c.StateId == request.StateId))
        {
            return BadRequest(new { message = "City not found for the selected state." });
        }

        return null;
    }

    private static LocationDto ToLocationDto(Location l) => new(
        l.Id, l.Name,
        l.RegionId, l.Region!.Name,
        l.StateId, l.State!.Name,
        l.CityId, l.City!.Name);

    // ============================ WAREHOUSES ============================

    [HttpGet("warehouses")]
    public async Task<ActionResult<List<WarehouseDto>>> GetWarehouses()
    {
        var warehouses = await _db.Warehouses
            .Include(w => w.Region).Include(w => w.State).Include(w => w.City).Include(w => w.Country)
            .OrderBy(w => w.Name)
            .ToListAsync();

        return Ok(warehouses.Select(ToWarehouseDto).ToList());
    }

    [HttpPost("warehouses")]
    public async Task<ActionResult<WarehouseDto>> CreateWarehouse(UpsertWarehouseRequest request)
    {
        if (!Enum.TryParse<WarehouseType>(request.WarehouseType, out var type))
        {
            return BadRequest(new { message = "Invalid warehouse type." });
        }

        var org = await GetCurrentOrganizationAsync();
        if (org.MaxWarehouses > 0 && await _db.Warehouses.CountAsync() >= org.MaxWarehouses)
        {
            return BadRequest(new { message = $"This organization is limited to {org.MaxWarehouses} warehouse(s). Contact the platform administrator to raise the limit." });
        }

        var warehouse = new Warehouse
        {
            Name = request.Name.Trim(),
            WarehouseType = type,
            RegionId = request.RegionId,
            StateId = request.StateId,
            CityId = request.CityId,
            CountryId = request.CountryId,
            SlaTargetMinutes = request.SlaTargetMinutes,
            DockOperatingHoursPerDay = request.DockOperatingHoursPerDay,
            ShiftHoursPerDay = request.ShiftHoursPerDay
        };
        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Warehouse", warehouse.Id, AuditAction.Created, $"Warehouse '{warehouse.Name}' created.", CurrentUserId, CurrentUserName);

        return await GetWarehouseDtoAsync(warehouse.Id);
    }

    [HttpPut("warehouses/{id:int}")]
    public async Task<ActionResult<WarehouseDto>> UpdateWarehouse(int id, UpsertWarehouseRequest request)
    {
        var warehouse = await _db.Warehouses.FindAsync(id);
        if (warehouse is null)
        {
            return NotFound();
        }

        if (!Enum.TryParse<WarehouseType>(request.WarehouseType, out var type))
        {
            return BadRequest(new { message = "Invalid warehouse type." });
        }

        warehouse.Name = request.Name.Trim();
        warehouse.WarehouseType = type;
        warehouse.RegionId = request.RegionId;
        warehouse.StateId = request.StateId;
        warehouse.CityId = request.CityId;
        warehouse.CountryId = request.CountryId;
        warehouse.SlaTargetMinutes = request.SlaTargetMinutes;
        warehouse.DockOperatingHoursPerDay = request.DockOperatingHoursPerDay;
        warehouse.ShiftHoursPerDay = request.ShiftHoursPerDay;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Warehouse", warehouse.Id, AuditAction.Updated, $"Warehouse '{warehouse.Name}' updated.", CurrentUserId, CurrentUserName);

        return await GetWarehouseDtoAsync(warehouse.Id);
    }

    [HttpDelete("warehouses/{id:int}")]
    public async Task<IActionResult> DeleteWarehouse(int id)
    {
        var warehouse = await _db.Warehouses.FindAsync(id);
        if (warehouse is null)
        {
            return NotFound();
        }

        _db.Warehouses.Remove(warehouse);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Warehouse", id, AuditAction.Deleted, $"Warehouse '{warehouse.Name}' deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    private async Task<ActionResult<WarehouseDto>> GetWarehouseDtoAsync(int id)
    {
        var warehouse = await _db.Warehouses
            .Include(w => w.Region).Include(w => w.State).Include(w => w.City).Include(w => w.Country)
            .FirstAsync(w => w.Id == id);
        return Ok(ToWarehouseDto(warehouse));
    }

    private static WarehouseDto ToWarehouseDto(Warehouse w) => new(
        w.Id, w.Name, w.WarehouseType.ToString(),
        w.RegionId, w.Region!.Name,
        w.StateId, w.State!.Name,
        w.CityId, w.City!.Name,
        w.CountryId, w.Country!.Name,
        w.SlaTargetMinutes, w.DockOperatingHoursPerDay, w.ShiftHoursPerDay);

    // ============================ DOCK BAYS ============================
    // Per-warehouse bay master - see DockBay for how this supersedes the free-text 1-50 entry.

    [HttpGet("dock-bays")]
    public async Task<ActionResult<List<DockBayDto>>> GetDockBays()
    {
        var bays = await _db.DockBays
            .Include(b => b.Warehouse)
            .OrderBy(b => b.Warehouse!.Name).ThenBy(b => b.Name)
            .ToListAsync();

        return Ok(bays.Select(b => new DockBayDto(b.Id, b.WarehouseId, b.Warehouse!.Name, b.Name, b.IsActive)).ToList());
    }

    [HttpPost("dock-bays")]
    public async Task<ActionResult<DockBayDto>> CreateDockBay(UpsertDockBayRequest request)
    {
        var error = await ValidateDockBayAsync(request, excludeId: null);
        if (error is not null)
        {
            return error;
        }

        var bay = new DockBay { WarehouseId = request.WarehouseId, Name = request.Name.Trim(), IsActive = request.IsActive };
        _db.DockBays.Add(bay);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("DockBay", bay.Id, AuditAction.Created, $"Dock bay '{bay.Name}' created.", CurrentUserId, CurrentUserName);

        return await GetDockBayDtoAsync(bay.Id);
    }

    [HttpPut("dock-bays/{id:int}")]
    public async Task<ActionResult<DockBayDto>> UpdateDockBay(int id, UpsertDockBayRequest request)
    {
        var bay = await _db.DockBays.FindAsync(id);
        if (bay is null)
        {
            return NotFound();
        }

        var error = await ValidateDockBayAsync(request, excludeId: id);
        if (error is not null)
        {
            return error;
        }

        bay.WarehouseId = request.WarehouseId;
        bay.Name = request.Name.Trim();
        bay.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("DockBay", bay.Id, AuditAction.Updated, $"Dock bay '{bay.Name}' updated.", CurrentUserId, CurrentUserName);

        return await GetDockBayDtoAsync(bay.Id);
    }

    [HttpDelete("dock-bays/{id:int}")]
    public async Task<IActionResult> DeleteDockBay(int id)
    {
        var bay = await _db.DockBays.FindAsync(id);
        if (bay is null)
        {
            return NotFound();
        }

        _db.DockBays.Remove(bay);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("DockBay", id, AuditAction.Deleted, $"Dock bay '{bay.Name}' deleted.", CurrentUserId, CurrentUserName);
        return NoContent();
    }

    private async Task<ActionResult?> ValidateDockBayAsync(UpsertDockBayRequest request, int? excludeId)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Bay name is required." });
        }

        if (!await _db.Warehouses.AnyAsync(w => w.Id == request.WarehouseId))
        {
            return BadRequest(new { message = "Warehouse not found." });
        }

        var name = request.Name.Trim();
        if (await _db.DockBays.AnyAsync(b => b.WarehouseId == request.WarehouseId && b.Name == name && b.Id != excludeId))
        {
            return BadRequest(new { message = $"Bay '{name}' already exists at this warehouse." });
        }

        return null;
    }

    private async Task<ActionResult<DockBayDto>> GetDockBayDtoAsync(int id)
    {
        var bay = await _db.DockBays.Include(b => b.Warehouse).FirstAsync(b => b.Id == id);
        return Ok(new DockBayDto(bay.Id, bay.WarehouseId, bay.Warehouse!.Name, bay.Name, bay.IsActive));
    }

    // ============================ USERS ============================

    [HttpGet("users")]
    public async Task<ActionResult<List<AdminUserDto>>> GetUsers()
    {
        var users = await _db.Users.Include(u => u.Warehouse).Include(u => u.Region)
            .OrderBy(u => u.UserName)
            .ToListAsync();

        return Ok(users.Select(u => new AdminUserDto(
            u.Id, u.UserName!, u.DisplayName, u.Role.ToString(),
            u.WarehouseId, u.Warehouse?.Name, u.RegionId, u.Region?.Name)).ToList());
    }

    [HttpPost("users")]
    public async Task<ActionResult<AdminUserDto>> CreateUser(CreateUserRequest request)
    {
        if (!Enum.TryParse<UserRole>(request.Role, out var role))
        {
            return BadRequest(new { message = "Invalid role." });
        }

        var org = await GetCurrentOrganizationAsync();
        if (org.MaxUsers > 0 && await _db.Users.CountAsync() >= org.MaxUsers)
        {
            return BadRequest(new { message = $"This organization is limited to {org.MaxUsers} user(s). Contact the platform administrator to raise the limit." });
        }

        var user = new ApplicationUser
        {
            UserName = request.UserName.Trim(),
            Email = $"{request.UserName.Trim()}@warehousegate.local",
            DisplayName = request.DisplayName.Trim(),
            Role = role,
            WarehouseId = request.WarehouseId,
            RegionId = request.RegionId,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });
        }

        await _audit.LogAsync("User", 0, AuditAction.Created, $"User '{user.UserName}' created with role {role}.", CurrentUserId, CurrentUserName);
        // Office's Assign Supervisor list needs to pick up new/re-warehoused supervisors without a
        // manual page reload - additive nudge only, the client just refetches api/office/supervisors.
        await _hub.Clients.Group(InwardHub.OfficeGroup).SendAsync("SupervisorsChanged");
        return await GetUserDtoAsync(user.Id);
    }

    [HttpPut("users/{id}")]
    public async Task<ActionResult<AdminUserDto>> UpdateUser(string id, UpdateUserRequest request)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (!Enum.TryParse<UserRole>(request.Role, out var role))
        {
            return BadRequest(new { message = "Invalid role." });
        }

        user.DisplayName = request.DisplayName.Trim();
        user.Role = role;
        user.WarehouseId = request.WarehouseId;
        user.RegionId = request.RegionId;
        await _userManager.UpdateAsync(user);

        await _audit.LogAsync("User", 0, AuditAction.Updated, $"User '{user.UserName}' updated.", CurrentUserId, CurrentUserName);
        await _hub.Clients.Group(InwardHub.OfficeGroup).SendAsync("SupervisorsChanged");
        return await GetUserDtoAsync(user.Id);
    }

    private async Task<ActionResult<AdminUserDto>> GetUserDtoAsync(string id)
    {
        var user = await _db.Users.Include(u => u.Warehouse).Include(u => u.Region).FirstAsync(u => u.Id == id);
        return Ok(new AdminUserDto(
            user.Id, user.UserName!, user.DisplayName, user.Role.ToString(),
            user.WarehouseId, user.Warehouse?.Name, user.RegionId, user.Region?.Name));
    }

    // ============================ AUDIT LOG ============================

    [HttpGet("audit-log")]
    public async Task<ActionResult<List<AuditLogDto>>> GetAuditLog([FromQuery] string? entityName)
    {
        var query = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityName))
        {
            query = query.Where(a => a.EntityName == entityName);
        }

        var logs = await query.OrderByDescending(a => a.ChangedAtUtc).Take(200).ToListAsync();
        return Ok(logs.Select(a => new AuditLogDto(
            a.Id, a.EntityName, a.EntityId, a.Action.ToString(), a.ChangedByName, a.ChangedAtUtc, a.Summary)).ToList());
    }
}
