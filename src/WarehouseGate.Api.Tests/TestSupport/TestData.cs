using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Tests.TestSupport;

// Minimal, valid entity graphs for each aggregate the tests need - just enough to satisfy
// required (non-nullable) FKs and the specific fields each service path under test reads.
// Every builder returns the entity un-added; callers add + SaveChanges so a test can tweak
// fields first.
public static class TestData
{
    // Region/State/City/Country are required (non-nullable) FKs on Warehouse - seeds one of
    // each with fixed ids 1, plus a second Region (2) for tests that need two distinct regions
    // (e.g. WarehouseScopeResolver's LogisticsManager scoping).
    public static void SeedBasicGeography(WarehouseGateDbContext db)
    {
        db.Countries.Add(new Country { Id = 1, Name = "India" });
        db.States.Add(new State { Id = 1, Name = "Maharashtra", CountryId = 1 });
        db.Cities.Add(new City { Id = 1, Name = "Mumbai", StateId = 1 });
        db.Regions.Add(new Region { Id = 1, Name = "West" });
        db.Regions.Add(new Region { Id = 2, Name = "South" });
    }

    public static Warehouse Warehouse(string name = "Test DC", int regionId = 1, int stateId = 1, int cityId = 1, int countryId = 1) => new()
    {
        Name = name,
        WarehouseType = WarehouseType.Warehouse,
        RegionId = regionId,
        StateId = stateId,
        CityId = cityId,
        CountryId = countryId
    };

    public static ApplicationUser User(string id, string userName, UserRole role, int? warehouseId = null, int? regionId = null) => new()
    {
        Id = id,
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        DisplayName = userName,
        Role = role,
        WarehouseId = warehouseId,
        RegionId = regionId
    };

    public static Vehicle Vehicle(string number) => new() { Number = number };

    public static PurchaseOrder PurchaseOrder(string poNumber, string supplierName = "Test Supplier") => new()
    {
        PONumber = poNumber,
        SupplierName = supplierName
    };

    public static DispatchOrder DispatchOrder(string number, string customerName = "Test Customer") => new()
    {
        DispatchOrderNumber = number,
        CustomerName = customerName,
        RequestedDate = DateTime.UtcNow
    };

    // Status/times are set by the caller - this only wires up the required FKs (Vehicle,
    // PurchaseOrder) and a sensible GateInTime so ordering-by-GateInTime tests behave.
    public static InwardTransaction InwardTransaction(
        Vehicle vehicle, PurchaseOrder purchaseOrder, int? warehouseId, string txnNumber) => new()
    {
        Vehicle = vehicle,
        PurchaseOrder = purchaseOrder,
        WarehouseId = warehouseId,
        InwardTxnNumber = txnNumber,
        GateInTime = DateTime.UtcNow,
        GateInBySecurityUserId = "security-1",
        Status = InwardStatus.GateIn
    };

    public static OutwardTransaction OutwardTransaction(
        DispatchOrder dispatchOrder, int? warehouseId, string txnNumber) => new()
    {
        DispatchOrder = dispatchOrder,
        WarehouseId = warehouseId,
        OutwardTxnNumber = txnNumber,
        CreatedTime = DateTime.UtcNow,
        CreatedByOfficeUserId = "office-1",
        Status = OutwardStatus.PickListGenerated
    };
}
