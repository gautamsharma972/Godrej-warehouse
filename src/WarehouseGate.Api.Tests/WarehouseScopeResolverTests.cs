using System.Security.Claims;
using WarehouseGate.Api.Services;
using WarehouseGate.Api.Tests.TestSupport;
using WarehouseGate.Domain;

namespace WarehouseGate.Api.Tests;

public class WarehouseScopeResolverTests : IClassFixture<SqliteDbFixture>
{
    private readonly SqliteDbFixture _fixture;

    public WarehouseScopeResolverTests(SqliteDbFixture fixture) => _fixture = fixture;

    private static ClaimsPrincipal PrincipalFor(string userId, string role) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, userId),
        new Claim(ClaimTypes.Role, role)
    }));

    [Fact]
    public async Task SuperAdmin_sees_every_warehouse_unscoped()
    {
        using var db = _fixture.CreateContext();
        TestData.SeedBasicGeography(db);
        var warehouseA = TestData.Warehouse("Mumbai DC", regionId: 1);
        var warehouseB = TestData.Warehouse("Bengaluru DC", regionId: 2);
        db.Warehouses.AddRange(warehouseA, warehouseB);
        db.Users.Add(TestData.User("super-1", "superadmin1", UserRole.SuperAdmin));
        await db.SaveChangesAsync();

        var resolver = new WarehouseScopeResolver(db);
        var scope = await resolver.ResolveAsync(PrincipalFor("super-1", nameof(UserRole.SuperAdmin)));

        Assert.Null(scope);
    }

    [Fact]
    public async Task LogisticsManager_sees_every_warehouse_in_their_own_region_only()
    {
        using var db = _fixture.CreateContext();
        TestData.SeedBasicGeography(db);
        var west1 = TestData.Warehouse("Mumbai DC", regionId: 1);
        var west2 = TestData.Warehouse("Pune DC", regionId: 1);
        var south1 = TestData.Warehouse("Bengaluru DC", regionId: 2);
        db.Warehouses.AddRange(west1, west2, south1);
        db.Users.Add(TestData.User("logi-1", "logistics1", UserRole.LogisticsManager, regionId: 1));
        await db.SaveChangesAsync();

        var resolver = new WarehouseScopeResolver(db);
        var scope = await resolver.ResolveAsync(PrincipalFor("logi-1", nameof(UserRole.LogisticsManager)));

        Assert.NotNull(scope);
        Assert.Equal(new[] { west1.Id, west2.Id }.OrderBy(x => x), scope!.OrderBy(x => x));
        Assert.DoesNotContain(south1.Id, scope);
    }

    [Fact]
    public async Task Office_sees_only_their_own_warehouse()
    {
        using var db = _fixture.CreateContext();
        TestData.SeedBasicGeography(db);
        var own = TestData.Warehouse("Mumbai DC", regionId: 1);
        var other = TestData.Warehouse("Bengaluru DC", regionId: 2);
        db.Warehouses.AddRange(own, other);
        await db.SaveChangesAsync(); // need warehouse ids assigned before the user references one

        db.Users.Add(TestData.User("office-1", "office1", UserRole.Office, warehouseId: own.Id));
        await db.SaveChangesAsync();

        var resolver = new WarehouseScopeResolver(db);
        var scope = await resolver.ResolveAsync(PrincipalFor("office-1", nameof(UserRole.Office)));

        Assert.NotNull(scope);
        Assert.Equal(new[] { own.Id }, scope);
    }
}
