using Microsoft.Extensions.Logging.Abstractions;
using WarehouseGate.Api.Services;
using WarehouseGate.Api.Tests.TestSupport;
using WarehouseGate.Domain;

namespace WarehouseGate.Api.Tests;

// A completed job's supervisor can no longer be reassigned (InwardService/OutwardService
// AssignSupervisorAsync both guard on this) - Office's UI already hides the Assign/Reassign
// button for Completed jobs, but the API must refuse it independently in case that ever slips.
public class AssignSupervisorGuardTests : IClassFixture<SqliteDbFixture>
{
    private readonly SqliteDbFixture _fixture;

    public AssignSupervisorGuardTests(SqliteDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Inward_AssignSupervisor_throws_when_job_already_completed()
    {
        using var db = _fixture.CreateContext();
        TestData.SeedBasicGeography(db);
        var warehouse = TestData.Warehouse();
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        var supervisor = TestData.User("sup-1", "supervisor1", UserRole.Supervisor, warehouse.Id);
        db.Users.Add(supervisor);

        var vehicle = TestData.Vehicle("MH-01-AB-1234");
        var po = TestData.PurchaseOrder("PO-1");
        var txn = TestData.InwardTransaction(vehicle, po, warehouse.Id, "TXN-1");
        txn.Status = InwardStatus.Completed;
        db.Vehicles.Add(vehicle);
        db.PurchaseOrders.Add(po);
        db.InwardTransactions.Add(txn);
        await db.SaveChangesAsync();

        var service = new InwardService(
            db, new FakePhotoStorageService(), new FakeHubContext(), NullLogger<InwardService>.Instance,
            new VehicleLogisticsSyncService(db), new OutwardLoadPlanService(db, new FakePhotoStorageService(), new FakeHubContext()),
            new AuditService(db));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AssignSupervisorAsync(txn.Id, supervisor.Id, warehouse.Id));
        Assert.Contains("already completed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Outward_AssignSupervisor_throws_when_job_already_completed()
    {
        using var db = _fixture.CreateContext();
        TestData.SeedBasicGeography(db);
        var warehouse = TestData.Warehouse();
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        var supervisor = TestData.User("sup-2", "supervisor2", UserRole.Supervisor, warehouse.Id);
        db.Users.Add(supervisor);

        var dispatchOrder = TestData.DispatchOrder("DO-1");
        var txn = TestData.OutwardTransaction(dispatchOrder, warehouse.Id, "OTXN-1");
        txn.Status = OutwardStatus.Completed;
        db.DispatchOrders.Add(dispatchOrder);
        db.OutwardTransactions.Add(txn);
        await db.SaveChangesAsync();

        var loadPlanService = new OutwardLoadPlanService(db, new FakePhotoStorageService(), new FakeHubContext());
        var service = new OutwardService(
            db, new FakePhotoStorageService(), new FakeHubContext(), NullLogger<OutwardService>.Instance,
            new WarehouseGate.LoadPlanning.LoadPlanningEngine(), loadPlanService,
            new VehicleLogisticsSyncService(db), new AuditService(db));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AssignSupervisorAsync(txn.Id, supervisor.Id, warehouse.Id));
        Assert.Contains("already completed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
