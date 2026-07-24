using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Tests.TestSupport;

// xunit shares one SqliteDbFixture instance across every [Fact] in a test class (that's what
// IClassFixture<T> means), so a single connection/schema here would leak seeded rows - and their
// fixed ids, see TestData.SeedBasicGeography - between otherwise-independent test methods. Each
// CreateContext() call instead opens its OWN ":memory:" connection and builds a fresh schema on
// it, giving every test a private, empty database while still reusing the fixture's lifetime to
// batch-dispose every connection it handed out.
public class SqliteDbFixture : IDisposable
{
    private readonly List<SqliteConnection> _connections = new();

    public WarehouseGateDbContext CreateContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        _connections.Add(connection);

        var options = new DbContextOptionsBuilder<WarehouseGateDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new WarehouseGateDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public void Dispose()
    {
        foreach (var connection in _connections)
        {
            connection.Dispose();
        }
    }
}
