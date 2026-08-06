using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using WarehouseGate.Infrastructure;

const string sourceConnectionString =
    "Server=(localdb)\\mssqllocaldb;Database=WarehouseGate;Trusted_Connection=True;MultipleActiveResultSets=true";
const string targetConnectionString =
    "Server=localhost;Database=warehousegate;User=warehousegate_app;Password=IOjh8IOXkE0ZfInK#9Gg;CharSet=utf8mb4;";

// Uses the app's own EF model metadata (not a hand-written table list) to compute a topological,
// FK-safe insert order - any entity referencing another via a foreign key gets inserted after its
// principal, so no manual ordering of ~40 tables was needed or risks going stale as the schema grows.
var optionsBuilder = new DbContextOptionsBuilder<WarehouseGateDbContext>();
optionsBuilder.UseMySql(targetConnectionString, ServerVersion.AutoDetect(targetConnectionString));
using var modelContext = new WarehouseGateDbContext(optionsBuilder.Options, new NullCurrentTenantProvider());

var entityTypes = modelContext.Model.GetEntityTypes().ToList();
var tableNames = new Dictionary<string, List<(string ColumnName, bool IsPrimaryKey, bool IsAutoIncrement)>>();
var dependencies = new Dictionary<string, HashSet<string>>();

foreach (var entityType in entityTypes)
{
    var tableName = entityType.GetTableName();
    if (tableName is null || tableNames.ContainsKey(tableName))
    {
        continue;
    }

    var primaryKeyColumns = entityType.FindPrimaryKey()?.Properties.Select(p => p.GetColumnName()).ToHashSet() ?? new HashSet<string>();
    var columns = entityType.GetProperties()
        // Excludes MySQL-only generated columns (e.g. Product.SkuCodeForUniqueness, the filtered-
        // unique-index workaround) - they don't exist in the source SQL Server schema at all, and
        // MySQL derives their value automatically from SkuCode, rejecting any explicit insert into them.
        .Where(p => p.GetComputedColumnSql() is null)
        .Select(p => (ColumnName: p.GetColumnName(), IsPrimaryKey: primaryKeyColumns.Contains(p.GetColumnName()), IsAutoIncrement: p.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd && primaryKeyColumns.Contains(p.GetColumnName())))
        .ToList();
    tableNames[tableName] = columns;
    dependencies[tableName] = new HashSet<string>();
}

foreach (var entityType in entityTypes)
{
    var tableName = entityType.GetTableName();
    if (tableName is null)
    {
        continue;
    }

    foreach (var fk in entityType.GetForeignKeys())
    {
        var principalTable = fk.PrincipalEntityType.GetTableName();
        if (principalTable is not null && principalTable != tableName)
        {
            dependencies[tableName].Add(principalTable);
        }
    }
}

// Standard DFS topological sort - "not visited" -> "in progress" -> "done", any residual cycle
// (shouldn't exist in this schema, but would show as a self-loop that's already excluded above)
// just falls back to declaration order for that node, which SET FOREIGN_KEY_CHECKS=0 covers.
var order = new List<string>();
var visited = new Dictionary<string, int>();

void Visit(string table)
{
    if (visited.TryGetValue(table, out var state))
    {
        if (state == 1)
        {
            return;
        }
        if (state == 2)
        {
            return;
        }
    }

    visited[table] = 1;
    foreach (var dependency in dependencies[table])
    {
        Visit(dependency);
    }
    visited[table] = 2;
    order.Add(table);
}

foreach (var table in tableNames.Keys)
{
    Visit(table);
}

Console.WriteLine($"Resolved insert order for {order.Count} tables.");

using var sourceConnection = new SqlConnection(sourceConnectionString);
await sourceConnection.OpenAsync();
using var targetConnection = new MySqlConnection(targetConnectionString);
await targetConnection.OpenAsync();

using (var disableChecks = new MySqlCommand("SET FOREIGN_KEY_CHECKS=0;", targetConnection))
{
    await disableChecks.ExecuteNonQueryAsync();
}

var rowCounts = new Dictionary<string, (long Source, long Target)>();

try
{
    foreach (var table in order)
    {
        var columns = tableNames[table];
        var columnNames = columns.Select(c => c.ColumnName).ToList();
        var quotedColumnList = string.Join(", ", columnNames.Select(c => $"`{c}`"));
        var parameterList = string.Join(", ", columnNames.Select((_, i) => $"@p{i}"));
        var insertSql = $"INSERT INTO `{table}` ({quotedColumnList}) VALUES ({parameterList});";

        var selectSql = $"SELECT {string.Join(", ", columnNames.Select(c => $"[{c}]"))} FROM [{table}];";
        using var selectCommand = new SqlCommand(selectSql, sourceConnection);
        using var reader = await selectCommand.ExecuteReaderAsync();

        long copied = 0;
        while (await reader.ReadAsync())
        {
            using var insertCommand = new MySqlCommand(insertSql, targetConnection);
            for (var i = 0; i < columnNames.Count; i++)
            {
                var value = reader.GetValue(i);
                insertCommand.Parameters.AddWithValue($"@p{i}", value == DBNull.Value ? DBNull.Value : value);
            }
            await insertCommand.ExecuteNonQueryAsync();
            copied++;
        }
        reader.Close();

        using var countCommand = new MySqlCommand($"SELECT COUNT(*) FROM `{table}`;", targetConnection);
        var targetCount = Convert.ToInt64(await countCommand.ExecuteScalarAsync());
        rowCounts[table] = (copied, targetCount);
        Console.WriteLine($"{table,-45} source={copied,6}  target={targetCount,6}  {(copied == targetCount ? "OK" : "MISMATCH")}");

        // Reset AUTO_INCREMENT past the highest migrated id so future app inserts never collide
        // with historical ones - only meaningful for tables with a single-column integer identity PK.
        var identityColumn = columns.FirstOrDefault(c => c.IsAutoIncrement);
        if (identityColumn.ColumnName is not null && copied > 0)
        {
            using var maxIdCommand = new MySqlCommand($"SELECT MAX(`{identityColumn.ColumnName}`) FROM `{table}`;", targetConnection);
            var maxId = await maxIdCommand.ExecuteScalarAsync();
            if (maxId is not null and not DBNull)
            {
                var nextId = Convert.ToInt64(maxId) + 1;
                using var alterCommand = new MySqlCommand($"ALTER TABLE `{table}` AUTO_INCREMENT = {nextId};", targetConnection);
                await alterCommand.ExecuteNonQueryAsync();
            }
        }
    }
}
finally
{
    using var enableChecks = new MySqlCommand("SET FOREIGN_KEY_CHECKS=1;", targetConnection);
    await enableChecks.ExecuteNonQueryAsync();
}

var mismatches = rowCounts.Where(kv => kv.Value.Source != kv.Value.Target).ToList();
Console.WriteLine();
Console.WriteLine(mismatches.Count == 0
    ? $"All {rowCounts.Count} tables copied with matching row counts."
    : $"{mismatches.Count} table(s) MISMATCHED: {string.Join(", ", mismatches.Select(m => m.Key))}");
