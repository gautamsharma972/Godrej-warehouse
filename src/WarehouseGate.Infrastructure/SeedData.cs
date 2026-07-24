using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WarehouseGate.Domain;

namespace WarehouseGate.Infrastructure;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<WarehouseGateDbContext>();
        await db.Database.MigrateAsync();

        await SeedMasterDataAsync(db);
        await SeedUsersAsync(services, db);
        await SeedPurchaseOrdersAsync(db);
        await SeedDispatchOrdersAsync(db);
        await SeedProductsAsync(db);
        await BackfillDispatchOrderLineProductsAsync(db);
        await BackfillDeliveryLocationsAsync(db);
        await SeedVehicleMastersAsync(db);
        await BackfillVehicleCapacityAsync(db);
        await SeedOutwardTransactionsAsync(services, db);
    }

    // India > (Maharashtra > Mumbai, Karnataka > Bengaluru) > (West, South) regions > 3 warehouses -
    // the minimum hierarchy needed to exercise the SuperAdmin master-data screens and give
    // LogisticsManager/Office test users a real region/warehouse to be scoped to.
    private static async Task SeedMasterDataAsync(WarehouseGateDbContext db)
    {
        var india = await db.Countries.FirstOrDefaultAsync(c => c.Name == "India");
        if (india is null)
        {
            india = new Country { Name = "India" };
            db.Countries.Add(india);
            await db.SaveChangesAsync();
        }

        var maharashtra = await GetOrAddStateAsync(db, "Maharashtra", india.Id);
        var karnataka = await GetOrAddStateAsync(db, "Karnataka", india.Id);
        await db.SaveChangesAsync();

        var mumbai = await GetOrAddCityAsync(db, "Mumbai", maharashtra.Id);
        var bengaluru = await GetOrAddCityAsync(db, "Bengaluru", karnataka.Id);
        await db.SaveChangesAsync();

        var west = await GetOrAddRegionAsync(db, "West");
        var south = await GetOrAddRegionAsync(db, "South");
        await db.SaveChangesAsync();

        await GetOrAddWarehouseAsync(db, "Mumbai DC", WarehouseType.Warehouse, west.Id, maharashtra.Id, mumbai.Id, india.Id);
        await GetOrAddWarehouseAsync(db, "Mumbai CPA", WarehouseType.CPA, west.Id, maharashtra.Id, mumbai.Id, india.Id);
        await GetOrAddWarehouseAsync(db, "Bengaluru DC", WarehouseType.Warehouse, south.Id, karnataka.Id, bengaluru.Id, india.Id);
        await db.SaveChangesAsync();
    }

    private static async Task<State> GetOrAddStateAsync(WarehouseGateDbContext db, string name, int countryId)
    {
        var existing = await db.States.FirstOrDefaultAsync(s => s.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        var state = new State { Name = name, CountryId = countryId };
        db.States.Add(state);
        return state;
    }

    private static async Task<City> GetOrAddCityAsync(WarehouseGateDbContext db, string name, int stateId)
    {
        var existing = await db.Cities.FirstOrDefaultAsync(c => c.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        var city = new City { Name = name, StateId = stateId };
        db.Cities.Add(city);
        return city;
    }

    private static async Task<Region> GetOrAddRegionAsync(WarehouseGateDbContext db, string name)
    {
        var existing = await db.Regions.FirstOrDefaultAsync(r => r.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        var region = new Region { Name = name };
        db.Regions.Add(region);
        return region;
    }

    private static async Task<Warehouse> GetOrAddWarehouseAsync(
        WarehouseGateDbContext db, string name, WarehouseType type, int regionId, int stateId, int cityId, int countryId)
    {
        var existing = await db.Warehouses.FirstOrDefaultAsync(w => w.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        var warehouse = new Warehouse
        {
            Name = name,
            WarehouseType = type,
            RegionId = regionId,
            StateId = stateId,
            CityId = cityId,
            CountryId = countryId
        };
        db.Warehouses.Add(warehouse);
        return warehouse;
    }

    private static async Task SeedUsersAsync(IServiceProvider services, WarehouseGateDbContext db)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        // Existing mobile-facing accounts get backfilled onto the first seeded warehouse so their
        // gate-ins are warehouse-attributed going forward - this needs no mobile app change since
        // the API stamps InwardTransaction/OutwardTransaction.WarehouseId from the checking-in
        // Security user's own WarehouseId at gate-in time.
        var defaultWarehouse = await db.Warehouses.FirstAsync(w => w.Name == "Mumbai DC");
        var westRegion = await db.Regions.FirstAsync(r => r.Name == "West");

        var security = await userManager.FindByNameAsync("security1");
        if (security is null)
        {
            security = new ApplicationUser
            {
                UserName = "security1",
                Email = "security1@warehousegate.local",
                DisplayName = "Gate Security",
                Role = UserRole.Security,
                WarehouseId = defaultWarehouse.Id,
                EmailConfirmed = true
            };
            await userManager.CreateAsync(security, "Pass123$");
        }
        else if (security.WarehouseId is null)
        {
            security.WarehouseId = defaultWarehouse.Id;
            await userManager.UpdateAsync(security);
        }

        var supervisor = await userManager.FindByNameAsync("supervisor1");
        if (supervisor is null)
        {
            supervisor = new ApplicationUser
            {
                UserName = "supervisor1",
                Email = "supervisor1@warehousegate.local",
                DisplayName = "Dock Supervisor",
                Role = UserRole.Supervisor,
                WarehouseId = defaultWarehouse.Id,
                EmailConfirmed = true
            };
            await userManager.CreateAsync(supervisor, "Pass123$");
        }
        else if (supervisor.WarehouseId is null)
        {
            supervisor.WarehouseId = defaultWarehouse.Id;
            await userManager.UpdateAsync(supervisor);
        }

        var supervisor2 = await userManager.FindByNameAsync("supervisor2");
        if (supervisor2 is null)
        {
            supervisor2 = new ApplicationUser
            {
                UserName = "supervisor2",
                Email = "supervisor2@warehousegate.local",
                DisplayName = "Second Dock Supervisor",
                Role = UserRole.Supervisor,
                WarehouseId = defaultWarehouse.Id,
                EmailConfirmed = true
            };
            await userManager.CreateAsync(supervisor2, "Pass123$");
        }
        else if (supervisor2.WarehouseId is null)
        {
            supervisor2.WarehouseId = defaultWarehouse.Id;
            await userManager.UpdateAsync(supervisor2);
        }

        var office = await userManager.FindByNameAsync("office1");
        if (office is null)
        {
            office = new ApplicationUser
            {
                UserName = "office1",
                Email = "office1@warehousegate.local",
                DisplayName = "Office Staff",
                Role = UserRole.Office,
                WarehouseId = defaultWarehouse.Id,
                EmailConfirmed = true
            };
            await userManager.CreateAsync(office, "Pass123$");
        }
        else if (office.WarehouseId is null)
        {
            office.WarehouseId = defaultWarehouse.Id;
            await userManager.UpdateAsync(office);
        }

        if (await userManager.FindByNameAsync("superadmin1") is null)
        {
            var superAdmin = new ApplicationUser
            {
                UserName = "superadmin1",
                Email = "superadmin1@warehousegate.local",
                DisplayName = "Super Admin",
                Role = UserRole.SuperAdmin,
                EmailConfirmed = true
            };
            await userManager.CreateAsync(superAdmin, "Pass123$");
        }

        if (await userManager.FindByNameAsync("logistics1") is null)
        {
            var logisticsManager = new ApplicationUser
            {
                UserName = "logistics1",
                Email = "logistics1@warehousegate.local",
                DisplayName = "Logistics Manager",
                Role = UserRole.LogisticsManager,
                RegionId = westRegion.Id,
                EmailConfirmed = true
            };
            await userManager.CreateAsync(logisticsManager, "Pass123$");
        }
    }

    // Reference data grows over time as new test scenarios are needed. Each item is seeded
    // independently (rather than gated behind a single "any rows?" check) so new entries can be
    // added later without being skipped just because earlier ones already exist in the dev DB.
    private static async Task SeedPurchaseOrdersAsync(WarehouseGateDbContext db)
    {
        // Expected delivery dates deliberately cover all three check-in scenarios:
        // PO-1001 within the tolerance window (clean check-in), PO-1002 outside it (soft
        // warning demonstrable), PO-1003 with no expected date at all (check skipped).
        await SeedPurchaseOrderIfMissingAsync(db, "PO-1001", "Acme Steel Works", DateTime.UtcNow.Date,
            ("MS Angle 50x50", 200m, "PCS"),
            ("MS Sheet 2mm", 50m, "PCS"));

        await SeedPurchaseOrderIfMissingAsync(db, "PO-1002", "Prime Packaging Ltd", DateTime.UtcNow.Date.AddDays(-10),
            ("Corrugated Box - Large", 500m, "PCS"));

        await SeedPurchaseOrderIfMissingAsync(db, "PO-1003", "Bharat Fasteners Co.", null,
            ("Hex Bolt M12", 1000m, "PCS"),
            ("Flat Washer M12", 2000m, "PCS"));

        await SeedVehicleIfMissingAsync(db, "MH04GT3312");
        await SeedVehicleIfMissingAsync(db, "GJ01AX7790");
        await SeedVehicleIfMissingAsync(db, "KA05MZ4521");
        await SeedVehicleIfMissingAsync(db, "MH20BZ9988");

        await db.SaveChangesAsync();
    }

    private static async Task SeedDispatchOrdersAsync(WarehouseGateDbContext db)
    {
        // DO-2009's fixture shape changed (3 SKUs/tiny qty -> 4 SKUs/qty sized against the
        // vehicle's capacity) after it could already exist from an earlier run - wipe the stale
        // 3-line version so the block below rebuilds it fresh, same "detect stale shape, reset
        // once" convention as the other backfills in this file.
        await ResetStaleDo2009FixtureAsync(db);

        await SeedDispatchOrderIfMissingAsync(db, "DO-2001", "Reliance Retail",
            ("MS Angle 50x50", 100m, "PCS", ""),
            ("MS Sheet 2mm", 20m, "PCS", ""));

        await SeedDispatchOrderIfMissingAsync(db, "DO-2002", "Vishal Mega Mart",
            ("Corrugated Box - Large", 300m, "PCS", ""));

        await SeedDispatchOrderIfMissingAsync(db, "DO-2003", "Big Bazaar",
            ("Hex Bolt M12", 400m, "PCS", ""),
            ("Flat Washer M12", 800m, "PCS", ""));

        await SeedDispatchOrderIfMissingAsync(db, "DO-2004", "D-Mart",
            ("LED Bulb 9W", 500m, "PCS", ""),
            ("Extension Cord 5m", 100m, "PCS", ""));

        await SeedDispatchOrderIfMissingAsync(db, "DO-2005", "Spencer's Retail",
            ("Steel Almirah", 20m, "PCS", ""));

        await SeedDispatchOrderIfMissingAsync(db, "DO-2006", "Star Bazaar",
            ("Ceiling Fan 48in", 150m, "PCS", ""));

        await SeedDispatchOrderIfMissingAsync(db, "DO-2007", "More Retail",
            ("PVC Pipe 2in", 250m, "PCS", ""),
            ("Pipe Elbow 2in", 250m, "PCS", ""));

        // Godrej FMCG dispatch - the primary fixture for testing the 3D Plan & Load screen:
        // four real-carton SKUs with distinct dimensions/weights on an 18-ton truck, sized to
        // land at roughly half the vehicle's volume and payload so there's plenty of open floor
        // to test free placement, collision, and rearranging without immediately overflowing.
        // Delivery locations are distinct dock/stop names so the Unloading View search has
        // something real to match against beyond product name.
        await SeedDispatchOrderIfMissingAsync(db, "DO-2008", "Reliance Smart Bazaar",
            ("Godrej No.1 Soap (125g) - Carton", 300m, "CTN", "Reliance Smart Bazaar - Dock 3"),
            ("Godrej Ezee Liquid Detergent - Carton", 150m, "CTN", "Reliance Smart Bazaar - Dock 3"),
            ("Good Knight Mosquito Coil - Carton", 400m, "CTN", "Reliance Smart Bazaar - Dock 1"),
            ("Godrej Aer Deodorant - Carton", 250m, "CTN", "Reliance Smart Bazaar - Dock 1"));

        // DO-2009: the full Dock-In-to-Complete walkthrough fixture (see
        // SeedOutwardTransactionsAsync for the matching pre-built load plan). All 4 Godrej SKUs,
        // quantities sized against the MH20BZ9988 18-ton / 960x240x260cm vehicle used at Dock-In:
        // ~7,750kg (43% of the 18,000kg payload) and ~795cm of the 960cm bed length - a
        // substantial, realistic partial load with headroom left on every axis, not a stress test.
        await SeedDispatchOrderIfMissingAsync(db, "DO-2009", "Metro Cash & Carry",
            ("Godrej No.1 Soap (125g) - Carton", 244m, "CTN", "Metro Cash & Carry - Dock 2"),
            ("Godrej Ezee Liquid Detergent - Carton", 80m, "CTN", "Metro Cash & Carry - Dock 2"),
            ("Good Knight Mosquito Coil - Carton", 256m, "CTN", "Metro Cash & Carry - Dock 2"),
            ("Godrej Aer Deodorant - Carton", 60m, "CTN", "Metro Cash & Carry - Dock 2"));

        // DO-2010: another full Dock-In-to-Complete walkthrough fixture, single-SKU this time -
        // 1440 cartons of soap alone. That quantity was picked to fit the vehicle bed exactly
        // (6 columns x 30 rows x 8 layers, all within its 8-layer stack cap) but its weight -
        // 1440 x 15kg = 21,600kg - is 3,600kg (20%) OVER the MH20BZ9988's 18,000kg payload limit
        // on purpose, so the Rule Validation card shows a genuine Capacity warning during Plan &
        // Load. The other 3 Godrej SKUs from DO-2009 don't fit alongside this one physically
        // (soap alone already spans 900 of the 960cm bed length), so this fixture is soap-only.
        await SeedDispatchOrderIfMissingAsync(db, "DO-2010", "Big Bazaar Central",
            ("Godrej No.1 Soap (125g) - Carton", 1440m, "CTN", "Big Bazaar Central - Dock 1"));

        await db.SaveChangesAsync();
    }

    // Weight (kg) and bounding-box dimensions (cm, L x W x H) per unit - drives the suggested
    // loading-sequence algorithm (heavier/bulkier items sort first). One row per product name
    // already used across the seeded Purchase/Dispatch orders above.
    private static async Task SeedProductsAsync(WarehouseGateDbContext db)
    {
        await SeedProductIfMissingAsync(db, "MS Angle 50x50", 4.0m, 200m, 5m, 5m, "STL-ANG-5050", WeightCategory.Heavy, true, 10);
        await SeedProductIfMissingAsync(db, "MS Sheet 2mm", 8.0m, 100m, 100m, 0.2m, "STL-SHT-2MM", WeightCategory.Heavy, true, 20);
        await SeedProductIfMissingAsync(db, "Corrugated Box - Large", 0.5m, 60m, 40m, 40m, "PKG-CTN-LRG", WeightCategory.Light, true, 15);
        await SeedProductIfMissingAsync(db, "Hex Bolt M12", 0.05m, 6m, 1.2m, 1.2m, "HW-BOLT-M12", WeightCategory.Medium, true, 30);
        await SeedProductIfMissingAsync(db, "Flat Washer M12", 0.01m, 2.4m, 2.4m, 0.2m, "HW-WASH-M12", WeightCategory.Light, true, 30);
        await SeedProductIfMissingAsync(db, "LED Bulb 9W", 0.1m, 6m, 6m, 11m, "ELEC-LED-9W", WeightCategory.Fragile, false, 1);
        await SeedProductIfMissingAsync(db, "Extension Cord 5m", 0.6m, 20m, 15m, 8m, "ELEC-EXT-5M", WeightCategory.Medium, true, 10);
        await SeedProductIfMissingAsync(db, "Steel Almirah", 45m, 90m, 45m, 185m, "FURN-ALM-STL", WeightCategory.Heavy, false, 1);
        await SeedProductIfMissingAsync(db, "Ceiling Fan 48in", 5.5m, 60m, 60m, 30m, "ELEC-FAN-48", WeightCategory.Fragile, false, 1);
        await SeedProductIfMissingAsync(db, "PVC Pipe 2in", 2.2m, 300m, 6m, 6m, "PLM-PIPE-2IN", WeightCategory.Medium, true, 6);
        await SeedProductIfMissingAsync(db, "Pipe Elbow 2in", 0.15m, 8m, 8m, 8m, "PLM-ELB-2IN", WeightCategory.Light, true, 20);

        // Godrej FMCG cartons for DO-2008 - dimensions match the shipping-carton sizes typical of
        // each category (soap/detergent/coil/deodorant), not the individual retail unit. Stack
        // limits/categories are set so the new NonStackable/MaxStackLayers rules have real cases to
        // catch in the existing DO-2008 fixture: Aer Deodorant is non-stackable (aerosol), the coil
        // carton caps out at 4 layers.
        await SeedProductIfMissingAsync(db, "Godrej No.1 Soap (125g) - Carton", 15m, 40m, 30m, 30m, "GCPL-SOAP-125", WeightCategory.Medium, true, 8, "#4f7cff");
        await SeedProductIfMissingAsync(db, "Godrej Ezee Liquid Detergent - Carton", 18m, 50m, 35m, 30m, "GCPL-EZEE-1L", WeightCategory.Heavy, true, 5, "#26c281");
        await SeedProductIfMissingAsync(db, "Good Knight Mosquito Coil - Carton", 8m, 28m, 20m, 20m, "GCPL-COIL-GK", WeightCategory.Fragile, true, 4, "#ffcf44");
        await SeedProductIfMissingAsync(db, "Godrej Aer Deodorant - Carton", 10m, 40m, 25m, 20m, "GCPL-AER-DEO", WeightCategory.Fragile, false, 1, "#8e6cff");

        await db.SaveChangesAsync();
    }

    private static async Task SeedProductIfMissingAsync(
        WarehouseGateDbContext db, string name, decimal weightKg, decimal lengthCm, decimal widthCm, decimal heightCm,
        string skuCode = "", WeightCategory category = WeightCategory.Medium, bool isStackable = true, int maxStackLayers = 99,
        string? colorHex = null)
    {
        var existing = await db.Products.FirstOrDefaultAsync(p => p.Name == name);
        if (existing is not null)
        {
            // SKU master fields were added after this row could already exist - EF's migration
            // gave pre-existing rows CLR-default values (SkuCode="", MaxStackLayers=0), which would
            // otherwise make every product look non-stackable. Backfill once, detected by the same
            // "still at CLR default" signature; never touch a row a SuperAdmin has since edited for
            // real (which will have a non-empty SkuCode).
            if (existing.SkuCode.Length == 0)
            {
                existing.SkuCode = skuCode;
                existing.Category = category;
                existing.IsStackable = isStackable;
                existing.MaxStackLayers = maxStackLayers;
                existing.ColorHex = colorHex;
            }

            return;
        }

        db.Products.Add(new Product
        {
            Name = name,
            WeightKg = weightKg,
            LengthCm = lengthCm,
            WidthCm = widthCm,
            HeightCm = heightCm,
            SkuCode = skuCode,
            Category = category,
            IsStackable = isStackable,
            MaxStackLayers = maxStackLayers,
            ColorHex = colorHex
        });
    }

    // DeliveryLocation was added after DO-2008 could already exist from an earlier run of this
    // seed - same backfill-once pattern as BackfillDispatchOrderLineProductsAsync below, gated on
    // the field still being at its CLR-default empty string so a real edit is never clobbered.
    private static async Task BackfillDeliveryLocationsAsync(WarehouseGateDbContext db)
    {
        var locationsByProductName = new Dictionary<string, string>
        {
            ["Godrej No.1 Soap (125g) - Carton"] = "Reliance Smart Bazaar - Dock 3",
            ["Godrej Ezee Liquid Detergent - Carton"] = "Reliance Smart Bazaar - Dock 3",
            ["Good Knight Mosquito Coil - Carton"] = "Reliance Smart Bazaar - Dock 1",
            ["Godrej Aer Deodorant - Carton"] = "Reliance Smart Bazaar - Dock 1"
        };

        var blankLines = await db.DispatchOrderLines.Where(l => l.DeliveryLocation == "").ToListAsync();
        foreach (var line in blankLines)
        {
            if (locationsByProductName.TryGetValue(line.ProductName, out var location))
            {
                line.DeliveryLocation = location;
            }
        }

        if (blankLines.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }

    // DispatchOrderLine.ProductId is optional and only ever set here (matched by ProductName) -
    // idempotent so it also fixes up any line seeded before this backfill existed.
    private static async Task BackfillDispatchOrderLineProductsAsync(WarehouseGateDbContext db)
    {
        var unlinkedLines = await db.DispatchOrderLines.Where(l => l.ProductId == null).ToListAsync();
        if (unlinkedLines.Count == 0)
        {
            return;
        }

        var products = await db.Products.ToDictionaryAsync(p => p.Name);
        foreach (var line in unlinkedLines)
        {
            if (products.TryGetValue(line.ProductName, out var product))
            {
                line.ProductId = product.Id;
            }
        }

        await db.SaveChangesAsync();
    }

    // Keeps the demo vehicle plates useful for loading tests after VehicleMaster moved from a
    // plate lookup to a type/category capacity profile.
    private static async Task BackfillVehicleCapacityAsync(WarehouseGateDbContext db)
    {
        var vehiclesNeedingCapacity = await db.Vehicles.Where(v => v.MaxWeightKg == null).ToListAsync();
        if (vehiclesNeedingCapacity.Count == 0)
        {
            return;
        }

        foreach (var vehicle in vehiclesNeedingCapacity)
        {
            var capacity = vehicle.Number switch
            {
                "MH04GT3312" => (MaxWeightKg: 2500m, LengthCm: 430m, WidthCm: 180m, HeightCm: 180m),
                "GJ01AX7790" => (MaxWeightKg: 3000m, LengthCm: 480m, WidthCm: 200m, HeightCm: 190m),
                "KA05MZ4521" => (MaxWeightKg: 1500m, LengthCm: 300m, WidthCm: 160m, HeightCm: 160m),
                "MH20BZ9988" => (MaxWeightKg: 18000m, LengthCm: 960m, WidthCm: 240m, HeightCm: 260m),
                _ => default
            };

            if (capacity.MaxWeightKg > 0)
            {
                vehicle.MaxWeightKg = capacity.MaxWeightKg;
                vehicle.LengthCm = capacity.LengthCm;
                vehicle.WidthCm = capacity.WidthCm;
                vehicle.HeightCm = capacity.HeightCm;
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedPurchaseOrderIfMissingAsync(
        WarehouseGateDbContext db, string poNumber, string supplierName, DateTime? expectedDeliveryDate,
        params (string ProductName, decimal Qty, string Uom)[] lines)
    {
        if (await db.PurchaseOrders.AnyAsync(p => p.PONumber == poNumber))
        {
            return;
        }

        db.PurchaseOrders.Add(new PurchaseOrder
        {
            PONumber = poNumber,
            SupplierName = supplierName,
            ExpectedDeliveryDate = expectedDeliveryDate,
            Lines = lines.Select(l => new PurchaseOrderLine
            {
                ProductName = l.ProductName,
                ExpectedQty = l.Qty,
                UnitOfMeasure = l.Uom
            }).ToList()
        });
    }

    private static async Task SeedDispatchOrderIfMissingAsync(
        WarehouseGateDbContext db, string dispatchOrderNumber, string customerName, params (string ProductName, decimal Qty, string Uom, string DeliveryLocation)[] lines)
    {
        if (await db.DispatchOrders.AnyAsync(d => d.DispatchOrderNumber == dispatchOrderNumber))
        {
            return;
        }

        db.DispatchOrders.Add(new DispatchOrder
        {
            DispatchOrderNumber = dispatchOrderNumber,
            CustomerName = customerName,
            RequestedDate = DateTime.UtcNow.Date,
            Lines = lines.Select(l => new DispatchOrderLine
            {
                ProductName = l.ProductName,
                OrderedQty = l.Qty,
                UnitOfMeasure = l.Uom,
                DeliveryLocation = l.DeliveryLocation
            }).ToList()
        });
    }

    // One OutwardTransaction per seeded DispatchOrder, deliberately parked at a different pipeline
    // stage each - so Supervisor (and Security's gate flow) testing can start directly at whichever
    // step is being worked on instead of manually walking every job through Claim -> Dock -> Load ->
    // Complete first. Idempotent per dispatch order (skips if a transaction already exists for it),
    // same convention as the other Seed*IfMissingAsync helpers below.
    private static async Task SeedOutwardTransactionsAsync(IServiceProvider services, WarehouseGateDbContext db)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var supervisor = await userManager.FindByNameAsync("supervisor1");
        var security = await userManager.FindByNameAsync("security1");
        var office = await userManager.FindByNameAsync("office1");
        if (supervisor is null || security is null || office is null)
        {
            return;
        }

        var vehicleGJ = await db.Vehicles.FirstAsync(v => v.Number == "GJ01AX7790");
        var vehicleKA = await db.Vehicles.FirstAsync(v => v.Number == "KA05MZ4521");
        var vehicleMH = await db.Vehicles.FirstAsync(v => v.Number == "MH04GT3312");
        var vehicleMH20 = await db.Vehicles.FirstAsync(v => v.Number == "MH20BZ9988");

        var now = DateTime.UtcNow;

        // DO-2001: fresh pick list, unclaimed - exercises the Claim flow.
        await CreateOutwardTransactionIfMissingAsync(db, "DO-2001", office.Id, t =>
        {
            t.CreatedTime = now.AddMinutes(-45);
        });

        // DO-2002: already claimed - exercises Dock-In directly.
        await CreateOutwardTransactionIfMissingAsync(db, "DO-2002", office.Id, t =>
        {
            t.CreatedTime = now.AddHours(-2);
            t.Status = OutwardStatus.Assigned;
            t.AssignedSupervisorUserId = supervisor.Id;
            t.AssignedTime = now.AddMinutes(-40);
        });

        // DO-2006 / DO-2007: same two stages as DO-2001/DO-2002 above, on dispatch orders that
        // haven't been used in prior manual testing - guarantees "available to claim" and
        // "already assigned" test fixtures exist even once DO-2001-2003 accumulate real history.
        await CreateOutwardTransactionIfMissingAsync(db, "DO-2006", office.Id, t =>
        {
            t.CreatedTime = now.AddMinutes(-20);
        });

        await CreateOutwardTransactionIfMissingAsync(db, "DO-2007", office.Id, t =>
        {
            t.CreatedTime = now.AddHours(-1).AddMinutes(-30);
            t.Status = OutwardStatus.Assigned;
            t.AssignedSupervisorUserId = supervisor.Id;
            t.AssignedTime = now.AddMinutes(-20);
        });

        // DO-2008: Godrej FMCG dispatch, gate-checked-in and docked with the 18-ton truck already
        // attached - lands directly in the Docked status the 3D Plan & Load screen requires, with
        // zero groups placed yet. The primary fixture for testing free placement end to end.
        await CreateOutwardTransactionIfMissingAsync(db, "DO-2008", office.Id, t =>
        {
            t.CreatedTime = now.AddHours(-1).AddMinutes(-15);
            t.Status = OutwardStatus.Docked;
            t.AssignedSupervisorUserId = supervisor.Id;
            t.AssignedTime = now.AddMinutes(-55);
            t.GateInTime = now.AddMinutes(-50);
            t.GateInBySecurityUserId = security.Id;
            t.DriverName = "Vijay Deshmukh";
            t.DriverMobile = "9867123450";
            t.TransporterName = "Godrej Consumer Products Fleet";
            t.GateName = "Gate 1";
            t.Vehicle = vehicleMH20;
            t.BayName = "Bay-5";
            t.DockInTime = now.AddMinutes(-20);
        });

        // DO-2009: still Assigned (not yet docked) with a load plan already built - the single
        // fixture that walks the entire docking journey start to end: Dock-In (type vehicle
        // number "MH20BZ9988" so its capacity matches the pre-built plan below) -> open the job
        // again to land in Plan & Load with 4 groups already placed and nothing confirmed yet ->
        // "Start Loading Confirmation" -> Confirm Loading (Start/Loaded per group - watch the
        // home-page progress percentage move in real time as each group resolves) -> photo
        // evidence -> dispatch ready -> Complete.
        await CreateOutwardTransactionIfMissingAsync(db, "DO-2009", office.Id, t =>
        {
            t.CreatedTime = now.AddMinutes(-10);
            t.Status = OutwardStatus.Assigned;
            t.AssignedSupervisorUserId = supervisor.Id;
            t.AssignedTime = now.AddMinutes(-5);
        });

        var do2009 = await db.OutwardTransactions
            .Include(t => t.LoadPlanOptions)
            .FirstOrDefaultAsync(t => t.DispatchOrder!.DispatchOrderNumber == "DO-2009");
        if (do2009 is not null && do2009.LoadPlanOptions.Count == 0)
        {
            var lines = await db.DispatchOrderLines
                .Where(l => l.DispatchOrder!.DispatchOrderNumber == "DO-2009")
                .OrderBy(l => l.Id)
                .ToListAsync();
            var soapLine = lines[0];
            var detergentLine = lines[1];
            var coilLine = lines[2];
            var deoLine = lines[3];

            // All 4 groups sit on the floor (Y=0), separated along Z (length) so their bounding
            // boxes never overlap regardless of X/Y overlap - unit dims come from
            // OrientationResolver's LWH mapping (DimX=ProductLength, DimY=ProductHeight,
            // DimZ=ProductWidth) times the Columns/Rows/Layers grid, matching the convention
            // OutwardLoadPlanService itself uses. Sized against MH20BZ9988's 18,000kg /
            // 960x240x260cm envelope: ~7,748kg total (43% of payload) and ~795cm of the 960cm
            // bed length used, with comfortable headroom left on every axis. Coil is stacked
            // exactly at its 4-layer cap (validates the boundary, not a violation); Aer is
            // single-layer only since its SKU master marks it non-stackable. Soap is stacked 7
            // layers high (within its 8-layer cap) rather than widened/lengthened, since its
            // footprint is already at the vehicle's full 240cm width and its Z-lane isn't shared
            // with any other group - keeps every other group's position untouched.
            var option = new OutwardLoadPlanOption
            {
                OutwardTransactionId = do2009.Id,
                Label = "Option A",
                IsSelected = true,
                CreatedAt = now.AddMinutes(-8),
                CreatedByUserId = supervisor.Id,
                Groups =
                {
                    new OutwardLoadPlanGroup
                    {
                        DispatchOrderLineId = soapLine.Id,
                        Quantity = 244,
                        ZoneLength = LoadZoneLength.Front,
                        ZoneWidth = LoadZoneWidth.Left,
                        ZoneHeight = LoadZoneHeight.Bottom,
                        PositionXCm = 0,
                        PositionYCm = 0,
                        PositionZCm = 0,
                        DimXCm = 240,
                        DimYCm = 210,
                        DimZCm = 180,
                        Orientation = LoadOrientation.LWH,
                        Columns = 6,
                        Layers = 7,
                        Rows = 6,
                        LoadSequence = 1,
                        Color = "#4f7cff",
                        CreatedAt = now.AddMinutes(-8),
                        CreatedByUserId = supervisor.Id
                    },
                    new OutwardLoadPlanGroup
                    {
                        DispatchOrderLineId = detergentLine.Id,
                        Quantity = 80,
                        ZoneLength = LoadZoneLength.Front,
                        ZoneWidth = LoadZoneWidth.Left,
                        ZoneHeight = LoadZoneHeight.Bottom,
                        PositionXCm = 0,
                        PositionYCm = 0,
                        PositionZCm = 190,
                        DimXCm = 200,
                        DimYCm = 120,
                        DimZCm = 175,
                        Orientation = LoadOrientation.LWH,
                        Columns = 4,
                        Layers = 4,
                        Rows = 5,
                        LoadSequence = 2,
                        Color = "#26c281",
                        CreatedAt = now.AddMinutes(-8),
                        CreatedByUserId = supervisor.Id
                    },
                    new OutwardLoadPlanGroup
                    {
                        DispatchOrderLineId = coilLine.Id,
                        Quantity = 256,
                        ZoneLength = LoadZoneLength.Middle,
                        ZoneWidth = LoadZoneWidth.Left,
                        ZoneHeight = LoadZoneHeight.Bottom,
                        PositionXCm = 0,
                        PositionYCm = 0,
                        PositionZCm = 375,
                        DimXCm = 224,
                        DimYCm = 80,
                        DimZCm = 160,
                        Orientation = LoadOrientation.LWH,
                        Columns = 8,
                        Layers = 4,
                        Rows = 8,
                        LoadSequence = 3,
                        Color = "#ffcf44",
                        CreatedAt = now.AddMinutes(-8),
                        CreatedByUserId = supervisor.Id
                    },
                    new OutwardLoadPlanGroup
                    {
                        DispatchOrderLineId = deoLine.Id,
                        Quantity = 60,
                        ZoneLength = LoadZoneLength.Middle,
                        ZoneWidth = LoadZoneWidth.Left,
                        ZoneHeight = LoadZoneHeight.Bottom,
                        PositionXCm = 0,
                        PositionYCm = 0,
                        PositionZCm = 545,
                        DimXCm = 240,
                        DimYCm = 20,
                        DimZCm = 250,
                        Orientation = LoadOrientation.LWH,
                        Columns = 6,
                        Layers = 1,
                        Rows = 10,
                        LoadSequence = 4,
                        Color = "#8e6cff",
                        CreatedAt = now.AddMinutes(-8),
                        CreatedByUserId = supervisor.Id
                    }
                }
            };

            db.OutwardLoadPlanOptions.Add(option);
            await db.SaveChangesAsync();
        }

        // DO-2010: same Dock-In-to-Complete walkthrough as DO-2009, but single-SKU and
        // deliberately over the vehicle's weight capacity - see the comment on its
        // SeedDispatchOrderIfMissingAsync call above for the exact numbers.
        await CreateOutwardTransactionIfMissingAsync(db, "DO-2010", office.Id, t =>
        {
            t.CreatedTime = now.AddMinutes(-8);
            t.Status = OutwardStatus.Assigned;
            t.AssignedSupervisorUserId = supervisor.Id;
            t.AssignedTime = now.AddMinutes(-4);
        });

        var do2010 = await db.OutwardTransactions
            .Include(t => t.LoadPlanOptions)
            .FirstOrDefaultAsync(t => t.DispatchOrder!.DispatchOrderNumber == "DO-2010");
        if (do2010 is not null && do2010.LoadPlanOptions.Count == 0)
        {
            var soapLine = await db.DispatchOrderLines
                .FirstAsync(l => l.DispatchOrder!.DispatchOrderNumber == "DO-2010");

            db.OutwardLoadPlanOptions.Add(new OutwardLoadPlanOption
            {
                OutwardTransactionId = do2010.Id,
                Label = "Option A",
                IsSelected = true,
                CreatedAt = now.AddMinutes(-6),
                CreatedByUserId = supervisor.Id,
                Groups =
                {
                    new OutwardLoadPlanGroup
                    {
                        DispatchOrderLineId = soapLine.Id,
                        Quantity = 1440,
                        ZoneLength = LoadZoneLength.Front,
                        ZoneWidth = LoadZoneWidth.Left,
                        ZoneHeight = LoadZoneHeight.Bottom,
                        PositionXCm = 0,
                        PositionYCm = 0,
                        PositionZCm = 0,
                        DimXCm = 240,
                        DimYCm = 240,
                        DimZCm = 900,
                        Orientation = LoadOrientation.LWH,
                        Columns = 6,
                        Layers = 8,
                        Rows = 30,
                        LoadSequence = 1,
                        Color = "#4f7cff",
                        CreatedAt = now.AddMinutes(-6),
                        CreatedByUserId = supervisor.Id
                    }
                }
            });
            await db.SaveChangesAsync();
        }

        // DO-2003: gate-checked-in by Security, then docked - exercises the picker checklist /
        // Start Loading step, and shows up in Security's Outward Status search.
        await CreateOutwardTransactionIfMissingAsync(db, "DO-2003", office.Id, t =>
        {
            t.CreatedTime = now.AddHours(-3);
            t.Status = OutwardStatus.Docked;
            t.AssignedSupervisorUserId = supervisor.Id;
            t.AssignedTime = now.AddHours(-1).AddMinutes(-30);
            t.GateInTime = now.AddHours(-1).AddMinutes(-50);
            t.GateInBySecurityUserId = security.Id;
            t.DriverName = "Suresh Patel";
            t.DriverMobile = "9823456789";
            t.TransporterName = "Gujarat Freight Carriers";
            t.GateName = "Gate 2";
            t.Vehicle = vehicleGJ;
            t.BayName = "Bay-3";
            t.DockInTime = now.AddMinutes(-25);
        });

        // DO-2004: loading in progress, one line already recorded - exercises load-line reorder,
        // exception reporting, dispatch-ready confirmation, and Complete.
        var doLine = await db.DispatchOrderLines.FirstAsync(l => l.DispatchOrder!.DispatchOrderNumber == "DO-2004");
        await CreateOutwardTransactionIfMissingAsync(db, "DO-2004", office.Id, t =>
        {
            t.CreatedTime = now.AddHours(-4);
            t.Status = OutwardStatus.Loading;
            t.AssignedSupervisorUserId = supervisor.Id;
            t.AssignedTime = now.AddHours(-2);
            t.Vehicle = vehicleKA;
            t.BayName = "Bay-1";
            t.DockInTime = now.AddHours(-1).AddMinutes(-45);
            t.LoadingStartTime = now.AddMinutes(-35);
            t.LoadLines.Add(new OutwardLoadLine
            {
                DispatchOrderLineId = doLine.Id,
                LoadedQty = doLine.OrderedQty,
                LoadSequence = 1
            });
        });

        // DO-2005: fully completed, not yet gated out - the standard "ready to exit" test case for
        // both Supervisor History and Security's Outward Exit search.
        var completedLines = await db.DispatchOrderLines.Where(l => l.DispatchOrder!.DispatchOrderNumber == "DO-2005").ToListAsync();
        await CreateOutwardTransactionIfMissingAsync(db, "DO-2005", office.Id, t =>
        {
            t.CreatedTime = now.AddHours(-6);
            t.Status = OutwardStatus.Completed;
            t.AssignedSupervisorUserId = supervisor.Id;
            t.AssignedTime = now.AddHours(-5);
            t.Vehicle = vehicleMH;
            t.BayName = "Bay-2";
            t.DockInTime = now.AddHours(-4).AddMinutes(-30);
            t.LoadingStartTime = now.AddHours(-4);
            t.DispatchReadyConfirmedAt = now.AddMinutes(-50);
            t.DockOutTime = now.AddMinutes(-45);
            foreach (var line in completedLines)
            {
                t.LoadLines.Add(new OutwardLoadLine { DispatchOrderLineId = line.Id, LoadedQty = line.OrderedQty, LoadSequence = t.LoadLines.Count + 1 });
            }
            t.Photos.Add(new OutwardPhotoEvidence { Type = OutwardPhotoType.VehicleLoaded, FilePath = "seed/outward-placeholder.jpg", CapturedAt = now.AddMinutes(-46) });
        });

        await db.SaveChangesAsync();

        var completedTransaction = await db.OutwardTransactions
            .FirstOrDefaultAsync(t => t.DispatchOrder!.DispatchOrderNumber == "DO-2005" && t.DispatchNote == null);
        if (completedTransaction is not null)
        {
            db.OutwardDispatchNotes.Add(new OutwardDispatchNote
            {
                OutwardTransactionId = completedTransaction.Id,
                DispatchNoteNumber = $"DN-{now:yyyyMMdd}-{completedTransaction.Id:D4}",
                GeneratedAt = now.AddMinutes(-45),
                IsPartial = false
            });
            await db.SaveChangesAsync();
        }
    }

    // See the comment above the call site in SeedDispatchOrdersAsync. No-ops once DO-2009
    // already has the current 4-line shape with the current Soap quantity (or doesn't exist yet
    // on a fresh install). Keyed on the Soap line's qty (not just line count) so tweaking a
    // quantity here also triggers a rebuild, not just changing the SKU count.
    private static async Task ResetStaleDo2009FixtureAsync(WarehouseGateDbContext db)
    {
        var dispatchOrder = await db.DispatchOrders
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.DispatchOrderNumber == "DO-2009");
        if (dispatchOrder is null)
        {
            return;
        }

        var soapLine = dispatchOrder.Lines.FirstOrDefault(l => l.ProductName == "Godrej No.1 Soap (125g) - Carton");
        if (dispatchOrder.Lines.Count == 4 && soapLine?.OrderedQty == 244m)
        {
            return;
        }

        var transaction = await db.OutwardTransactions
            .Include(t => t.LoadPlanOptions).ThenInclude(o => o.Groups)
            .FirstOrDefaultAsync(t => t.DispatchOrderId == dispatchOrder.Id);
        if (transaction is not null)
        {
            foreach (var option in transaction.LoadPlanOptions)
            {
                db.OutwardLoadPlanGroups.RemoveRange(option.Groups);
            }
            db.OutwardLoadPlanOptions.RemoveRange(transaction.LoadPlanOptions);
            db.OutwardTransactions.Remove(transaction);
        }

        db.DispatchOrderLines.RemoveRange(dispatchOrder.Lines);
        db.DispatchOrders.Remove(dispatchOrder);
        await db.SaveChangesAsync();
    }

    private static async Task CreateOutwardTransactionIfMissingAsync(
        WarehouseGateDbContext db, string dispatchOrderNumber, string officeUserId, Action<OutwardTransaction> configure)
    {
        var dispatchOrder = await db.DispatchOrders.FirstOrDefaultAsync(d => d.DispatchOrderNumber == dispatchOrderNumber)
            ?? throw new InvalidOperationException($"Dispatch order '{dispatchOrderNumber}' must be seeded before its outward transaction.");

        if (await db.OutwardTransactions.AnyAsync(t => t.DispatchOrderId == dispatchOrder.Id))
        {
            return;
        }

        var transaction = new OutwardTransaction
        {
            DispatchOrderId = dispatchOrder.Id,
            Status = OutwardStatus.PickListGenerated,
            CreatedTime = DateTime.UtcNow,
            CreatedByOfficeUserId = officeUserId
        };
        configure(transaction);

        db.OutwardTransactions.Add(transaction);
        await db.SaveChangesAsync();

        // Mirrors OutwardService.GeneratePickListAsync, which only knows its own Id after the
        // first save.
        transaction.OutwardTxnNumber = $"OUT-{transaction.CreatedTime:yyyyMMdd}-{transaction.Id:D4}";
        await db.SaveChangesAsync();
    }

    private static async Task SeedVehicleIfMissingAsync(WarehouseGateDbContext db, string number)
    {
        if (await db.Vehicles.AnyAsync(v => v.Number == number))
        {
            return;
        }

        db.Vehicles.Add(new Vehicle { Number = number });
    }

    // Covers the realistic spread of vehicles seen at an Indian FMCG warehouse gate, from the
    // smallest last-mile tempo up to a 32-ton multi-axle trailer, plus a reefer for cold-chain
    // loads - so SuperAdmin's Vehicle Masters screen has a full reference set out of the box
    // instead of just the three profiles needed by the original DO-2008/DO-2009 fixtures.
    private static async Task SeedVehicleMastersAsync(WarehouseGateDbContext db)
    {
        var threeWheeler = await GetOrAddVehicleTypeAsync(db, "Three-Wheeler (Tempo)");
        var pickupTruck = await GetOrAddVehicleTypeAsync(db, "Pickup Truck");
        var miniTruck = await GetOrAddVehicleTypeAsync(db, "Mini Truck");
        var closedBodyTruck = await GetOrAddVehicleTypeAsync(db, "Closed Body Truck");
        var openTruck = await GetOrAddVehicleTypeAsync(db, "Open Truck");
        var containerTruck = await GetOrAddVehicleTypeAsync(db, "Container Truck");
        var multiAxleTrailer = await GetOrAddVehicleTypeAsync(db, "Multi-Axle Trailer");
        var refrigeratedTruck = await GetOrAddVehicleTypeAsync(db, "Refrigerated Truck");

        var lightCommercial = await GetOrAddVehicleCategoryAsync(db, "Light Commercial");
        var mediumCommercial = await GetOrAddVehicleCategoryAsync(db, "Medium Commercial");
        var heavyCommercial = await GetOrAddVehicleCategoryAsync(db, "Heavy Commercial");
        var eighteenTon = await GetOrAddVehicleCategoryAsync(db, "18 Ton");
        var multiAxle32Ton = await GetOrAddVehicleCategoryAsync(db, "Multi-Axle (32 Ton)");
        var coldChain = await GetOrAddVehicleCategoryAsync(db, "Cold Chain");

        await db.SaveChangesAsync();

        // Three-wheeler tempo (e.g. Piaggio Ape/Mahindra Champion) - smallest goods carrier used
        // for last-mile/short-haul loads.
        await SeedVehicleMasterIfMissingAsync(db, threeWheeler.Id, lightCommercial.Id, 500m, 210m, 140m, 140m);

        // Pickup truck (e.g. Tata Ace/Ashok Leyland Dost) - the "Chota Hathi" class, smaller than
        // the Mini Truck profile below.
        await SeedVehicleMasterIfMissingAsync(db, pickupTruck.Id, lightCommercial.Id, 750m, 220m, 150m, 150m);

        await SeedVehicleMasterIfMissingAsync(db, miniTruck.Id, lightCommercial.Id, 1500m, 300m, 160m, 160m);
        await SeedVehicleMasterIfMissingAsync(db, openTruck.Id, mediumCommercial.Id, 3000m, 480m, 200m, 190m);

        // Closed/box-body truck (e.g. Eicher 14-17ft) - weather-protected FMCG loads too large
        // for the Open Truck profile.
        await SeedVehicleMasterIfMissingAsync(db, closedBodyTruck.Id, heavyCommercial.Id, 7000m, 580m, 220m, 210m);

        // 18-ton truck (9600x2400x2600mm internal, 18000kg payload) - the standard reference
        // vehicle profile for the Godrej FMCG dispatch order (DO-2008).
        await SeedVehicleMasterIfMissingAsync(db, containerTruck.Id, eighteenTon.Id, 18000m, 960m, 240m, 260m);

        // 32-ton multi-axle trailer (40ft) - the largest vehicle profile, for full-truckload
        // long-haul dispatches.
        await SeedVehicleMasterIfMissingAsync(db, multiAxleTrailer.Id, multiAxle32Ton.Id, 25000m, 1200m, 240m, 270m);

        // Reefer/refrigerated truck for cold-chain FMCG (frozen/chilled) loads.
        await SeedVehicleMasterIfMissingAsync(db, refrigeratedTruck.Id, coldChain.Id, 5000m, 500m, 220m, 220m);

        await db.SaveChangesAsync();
    }

    private static async Task<VehicleType> GetOrAddVehicleTypeAsync(WarehouseGateDbContext db, string name)
    {
        var existing = await db.VehicleTypes.FirstOrDefaultAsync(t => t.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        var type = new VehicleType { Name = name };
        db.VehicleTypes.Add(type);
        return type;
    }

    private static async Task<VehicleCategory> GetOrAddVehicleCategoryAsync(WarehouseGateDbContext db, string name)
    {
        var existing = await db.VehicleCategories.FirstOrDefaultAsync(c => c.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        var category = new VehicleCategory { Name = name };
        db.VehicleCategories.Add(category);
        return category;
    }

    private static async Task SeedVehicleMasterIfMissingAsync(
        WarehouseGateDbContext db, int vehicleTypeId, int vehicleCategoryId,
        decimal maxWeightKg, decimal lengthCm, decimal widthCm, decimal heightCm)
    {
        var existing = await db.VehicleMasters.FirstOrDefaultAsync(v =>
            v.VehicleTypeId == vehicleTypeId && v.VehicleCategoryId == vehicleCategoryId);
        if (existing is not null)
        {
            if (existing.MaxWeightKg is null)
            {
                existing.MaxWeightKg = maxWeightKg;
                existing.LengthCm = lengthCm;
                existing.WidthCm = widthCm;
                existing.HeightCm = heightCm;
            }
            return;
        }

        db.VehicleMasters.Add(new VehicleMaster
        {
            VehicleTypeId = vehicleTypeId,
            VehicleCategoryId = vehicleCategoryId,
            MaxWeightKg = maxWeightKg,
            LengthCm = lengthCm,
            WidthCm = widthCm,
            HeightCm = heightCm
        });
    }
}
