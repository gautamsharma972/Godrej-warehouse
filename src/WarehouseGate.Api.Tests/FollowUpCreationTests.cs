using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WarehouseGate.Api.Services;
using WarehouseGate.Api.Tests.TestSupport;
using WarehouseGate.Domain;

namespace WarehouseGate.Api.Tests;

// Covers the two places a completion path turns a log-warning into a real, visible Office
// to-do (see InwardService.CompleteAsync's hasExceptions branch and OutwardService.CompleteAsync's
// isPartial branch) - both create a FollowUpTask row that the Office Follow-ups page reads.
public class FollowUpCreationTests : IClassFixture<SqliteDbFixture>
{
    private readonly SqliteDbFixture _fixture;

    public FollowUpCreationTests(SqliteDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Inward_complete_with_a_damaged_line_creates_an_InwardException_followup()
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
        var line = new PurchaseOrderLine { PurchaseOrder = po, ProductName = "Widget", ExpectedQty = 10 };
        po.Lines.Add(line);

        var txn = TestData.InwardTransaction(vehicle, po, warehouse.Id, "TXN-1");
        txn.Status = InwardStatus.Inspecting;
        txn.AssignedSupervisorUserId = supervisor.Id;
        txn.Photos.Add(new PhotoEvidence { Type = PhotoType.MaterialBefore, FilePath = "x.jpg", CapturedAt = DateTime.UtcNow });
        txn.InspectionLines.Add(new InspectionLine { PurchaseOrderLine = line, ReceivedQty = 5, Condition = MaterialCondition.Damaged });

        db.Vehicles.Add(vehicle);
        db.PurchaseOrders.Add(po);
        db.InwardTransactions.Add(txn);
        await db.SaveChangesAsync();

        var service = new InwardService(
            db, new FakePhotoStorageService(), new FakeHubContext(), NullLogger<InwardService>.Instance,
            new VehicleLogisticsSyncService(db), new OutwardLoadPlanService(db, new FakePhotoStorageService(), new FakeHubContext()),
            new AuditService(db));

        var dto = await service.CompleteAsync(txn.Id, supervisor.Id);

        Assert.Equal("Completed", dto.Status);
        var followUp = await db.FollowUpTasks.SingleAsync(f => f.EntityId == txn.Id && f.EntityName == "InwardTransaction");
        Assert.Equal(FollowUpType.InwardException, followUp.Type);
        Assert.Equal(FollowUpStatus.Open, followUp.Status);
        Assert.Equal(warehouse.Id, followUp.WarehouseId);
    }

    [Fact]
    public async Task Outward_complete_with_a_short_loaded_line_creates_a_PartialLoadDispatch_followup()
    {
        using var db = _fixture.CreateContext();
        TestData.SeedBasicGeography(db);
        var warehouse = TestData.Warehouse();
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        var supervisor = TestData.User("sup-2", "supervisor2", UserRole.Supervisor, warehouse.Id);
        db.Users.Add(supervisor);

        var dispatchOrder = TestData.DispatchOrder("DO-1");
        var line = new DispatchOrderLine { DispatchOrder = dispatchOrder, ProductName = "Widget", OrderedQty = 100 };
        dispatchOrder.Lines.Add(line);

        var txn = TestData.OutwardTransaction(dispatchOrder, warehouse.Id, "OTXN-1");
        txn.Status = OutwardStatus.Loading;
        txn.AssignedSupervisorUserId = supervisor.Id;
        txn.DispatchReadyConfirmedAt = DateTime.UtcNow;
        txn.Photos.Add(new OutwardPhotoEvidence { Type = OutwardPhotoType.MaterialLoaded, FilePath = "x.jpg", CapturedAt = DateTime.UtcNow });
        txn.LoadLines.Add(new OutwardLoadLine { DispatchOrderLine = line, LoadedQty = 60, LoadSequence = 1 }); // short of the 100 ordered

        db.DispatchOrders.Add(dispatchOrder);
        db.OutwardTransactions.Add(txn);
        await db.SaveChangesAsync();

        var loadPlanService = new OutwardLoadPlanService(db, new FakePhotoStorageService(), new FakeHubContext());
        var service = new OutwardService(
            db, new FakePhotoStorageService(), new FakeHubContext(), NullLogger<OutwardService>.Instance,
            new WarehouseGate.LoadPlanning.LoadPlanningEngine(), loadPlanService,
            new VehicleLogisticsSyncService(db), new AuditService(db));

        var dto = await service.CompleteAsync(txn.Id, supervisor.Id);

        Assert.Equal("Completed", dto.Status);
        var followUp = await db.FollowUpTasks.SingleAsync(f => f.EntityId == txn.Id && f.EntityName == "OutwardTransaction");
        Assert.Equal(FollowUpType.PartialLoadDispatch, followUp.Type);
        Assert.Equal(FollowUpStatus.Open, followUp.Status);
        Assert.Equal(warehouse.Id, followUp.WarehouseId);
    }
}
