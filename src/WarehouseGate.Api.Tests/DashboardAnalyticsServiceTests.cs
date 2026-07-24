using WarehouseGate.Api.Services;
using WarehouseGate.Api.Tests.TestSupport;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Tests;

public class DashboardAnalyticsServiceTests : IClassFixture<SqliteDbFixture>
{
    private readonly SqliteDbFixture _fixture;

    public DashboardAnalyticsServiceTests(SqliteDbFixture fixture) => _fixture = fixture;

    // Adds a completed InwardTransaction (with one InspectionLine, condition Ok) for the given
    // supervisor, with the given received quantity and processing duration, completing "now".
    private static InwardTransaction AddCompletedInwardJob(
        WarehouseGateDbContext db, Warehouse warehouse, string supervisorId, decimal receivedQty, TimeSpan duration, string bayName = "Bay-1")
    {
        var vehicle = new Vehicle { Number = $"TEST-{Guid.NewGuid():N}"[..12] };
        var po = new PurchaseOrder { PONumber = $"PO-{Guid.NewGuid():N}"[..10], SupplierName = "Supplier" };
        var line = new PurchaseOrderLine { PurchaseOrder = po, ProductName = "Widget", ExpectedQty = receivedQty };
        po.Lines.Add(line);

        var now = DateTime.UtcNow;
        var start = now - duration;
        var txn = new InwardTransaction
        {
            Vehicle = vehicle,
            PurchaseOrder = po,
            WarehouseId = warehouse.Id,
            InwardTxnNumber = $"TXN-{Guid.NewGuid():N}"[..10],
            GateInTime = start,
            GateInBySecurityUserId = "security-1",
            AssignedSupervisorUserId = supervisorId,
            BayName = bayName,
            UnloadingStartTime = start,
            DockOutTime = now,
            Status = InwardStatus.Completed
        };
        txn.InspectionLines.Add(new InspectionLine
        {
            PurchaseOrderLine = line,
            ReceivedQty = receivedQty,
            Condition = MaterialCondition.Ok
        });

        db.Vehicles.Add(vehicle);
        db.PurchaseOrders.Add(po);
        db.InwardTransactions.Add(txn);
        return txn;
    }

    [Fact]
    public async Task Leaderboard_uses_weighted_total_quantity_over_total_hours_not_average_of_job_rates()
    {
        using var db = _fixture.CreateContext();
        TestData.SeedBasicGeography(db);
        var warehouse = TestData.Warehouse();
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        db.Users.Add(TestData.User("sup-1", "supervisor1", UserRole.Supervisor, warehouse.Id));
        // Job A: 60 units / 1 hour -> individual rate 60/hr. Job B: 100 units / 10 hours -> individual
        // rate 10/hr. A simple average of the two rates would be 35/hr; the weighted total is
        // 160 units / 11 hours = 14.5/hr - if the service reports 14.5, it's weighting correctly.
        AddCompletedInwardJob(db, warehouse, "sup-1", receivedQty: 60, duration: TimeSpan.FromHours(1));
        AddCompletedInwardJob(db, warehouse, "sup-1", receivedQty: 100, duration: TimeSpan.FromHours(10));
        await db.SaveChangesAsync();

        var result = await new DashboardAnalyticsService(db).BuildAsync(null);

        var entry = Assert.Single(result.Leaderboard);
        Assert.Equal("sup-1", entry.SupervisorUserId);
        Assert.Equal(2, entry.VehiclesHandled);
        Assert.Equal(160, entry.TotalQuantity);
        Assert.Equal(14.5m, entry.BoxesPerHour);
    }

    [Fact]
    public async Task SupervisorPerformance_excludes_zero_duration_jobs_from_the_average()
    {
        using var db = _fixture.CreateContext();
        TestData.SeedBasicGeography(db);
        var warehouse = TestData.Warehouse();
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        db.Users.Add(TestData.User("sup-2", "supervisor2", UserRole.Supervisor, warehouse.Id));
        AddCompletedInwardJob(db, warehouse, "sup-2", receivedQty: 10, duration: TimeSpan.FromHours(1));
        // Zero-duration job (bad/legacy data: start == end) - ProcessingMinutes is null for this
        // one and must be excluded, not counted as a free 0-minute job dragging the average down.
        AddCompletedInwardJob(db, warehouse, "sup-2", receivedQty: 5, duration: TimeSpan.Zero);
        await db.SaveChangesAsync();

        var result = await new DashboardAnalyticsService(db).BuildAsync(null);

        var entry = Assert.Single(result.SupervisorPerformance);
        Assert.Equal(2, entry.VehiclesThisMonth); // both still count as vehicles handled
        Assert.Equal(60m, entry.AvgProcessingMinutes); // only the 60-minute job feeds the average
    }

    [Fact]
    public async Task SlaCompliance_judges_each_job_against_its_own_warehouse_target_with_default_fallback()
    {
        using var db = _fixture.CreateContext();
        TestData.SeedBasicGeography(db);
        var warehouseWithTarget = TestData.Warehouse("Has Target", regionId: 1);
        warehouseWithTarget.SlaTargetMinutes = 45;
        var warehouseWithoutTarget = TestData.Warehouse("No Target", regionId: 2);
        db.Warehouses.AddRange(warehouseWithTarget, warehouseWithoutTarget);
        await db.SaveChangesAsync();

        db.Users.Add(TestData.User("sup-3", "supervisor3", UserRole.Supervisor));
        // Both jobs take exactly 40 minutes: within warehouseWithTarget's explicit 45-minute
        // target (meets SLA), but outside the 30-minute system default that warehouseWithoutTarget
        // falls back to (misses SLA). If the service used one global constant instead of each
        // warehouse's own setting, both would land on the same side of the line.
        AddCompletedInwardJob(db, warehouseWithTarget, "sup-3", receivedQty: 1, duration: TimeSpan.FromMinutes(40));
        AddCompletedInwardJob(db, warehouseWithoutTarget, "sup-3", receivedQty: 1, duration: TimeSpan.FromMinutes(40));
        await db.SaveChangesAsync();

        var result = await new DashboardAnalyticsService(db).BuildAsync(null);

        Assert.Equal(2, result.Kpis.SlaTotalVehicles);
        Assert.Equal(1, result.Kpis.SlaVehiclesMeetingTarget);
        Assert.Equal(50.0m, result.Kpis.SlaCompliancePct);
    }

    [Fact]
    public async Task DockUtilization_uses_active_DockBay_count_instead_of_the_distinct_bay_heuristic_when_configured()
    {
        using var db = _fixture.CreateContext();
        TestData.SeedBasicGeography(db);
        var warehouseNoBayMaster = TestData.Warehouse("No Bay Master", regionId: 1);
        var warehouseWithBayMaster = TestData.Warehouse("Has Bay Master", regionId: 2);
        db.Warehouses.AddRange(warehouseNoBayMaster, warehouseWithBayMaster);
        await db.SaveChangesAsync();

        // Both warehouses see the identical activity pattern (one job, one bay name, 2 active
        // hours) - the only difference is warehouseWithBayMaster has 3 active dock bays on file.
        db.Users.Add(TestData.User("sup-4a", "supervisor4a", UserRole.Supervisor));
        db.Users.Add(TestData.User("sup-4b", "supervisor4b", UserRole.Supervisor));
        AddCompletedInwardJob(db, warehouseNoBayMaster, "sup-4a", receivedQty: 1, duration: TimeSpan.FromHours(2), bayName: "Bay-1");
        AddCompletedInwardJob(db, warehouseWithBayMaster, "sup-4b", receivedQty: 1, duration: TimeSpan.FromHours(2), bayName: "Bay-1");
        db.DockBays.Add(new DockBay { WarehouseId = warehouseWithBayMaster.Id, Name = "Bay-1", IsActive = true });
        db.DockBays.Add(new DockBay { WarehouseId = warehouseWithBayMaster.Id, Name = "Bay-2", IsActive = true });
        db.DockBays.Add(new DockBay { WarehouseId = warehouseWithBayMaster.Id, Name = "Bay-3", IsActive = true });
        await db.SaveChangesAsync();

        var service = new DashboardAnalyticsService(db);
        // No-bay-master warehouse: heuristic sees 1 distinct bay name -> 1 lane -> available
        // hours = 1 x 10 (default) x 7 days = 70 -> 2 / 70 x 100 = 2.9%.
        var withoutMaster = await service.BuildAsync(new List<int> { warehouseNoBayMaster.Id });
        // Bay-master warehouse: 3 active bays -> available hours = 3 x 10 x 7 = 210 -> 2 / 210 x 100 = 1.0%.
        var withMaster = await service.BuildAsync(new List<int> { warehouseWithBayMaster.Id });

        Assert.Equal(2.9m, withoutMaster.Kpis.DockUtilizationPct);
        Assert.Equal(1.0m, withMaster.Kpis.DockUtilizationPct);
    }
}
