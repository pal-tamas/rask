using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// HasJsonIndex (#1112): an expression index SQLite actually uses for the filter EF writes. An expression index is
// used only for a byte-identical expression, so every assertion that matters is about the QUERY PLAN of the SQL
// EF generates, not about the DDL.
public sealed class JsonIndexTests : IDisposable
{
    private readonly InMemorySqlite _database = new();

    [Fact]
    public async Task A_filter_on_a_top_level_json_value_is_an_index_search()
    {
        await using var db = await SeededAsync();

        var plan = await PlanAsync(db, db.Orders.Where(o => o.Meta.Status == "open").ToQueryString());

        Assert.Contains("USING INDEX ix_Orders_Meta_status", plan, StringComparison.Ordinal);
        Assert.Equal([2], await db.Orders.Where(o => o.Meta.Status == "open").Select(o => o.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_filter_on_a_nested_json_value_is_an_index_search()
    {
        // EF spells a nested path '$.Address.City' and a top-level one 'Status' — the index has to follow suit.
        await using var db = await SeededAsync();

        var plan = await PlanAsync(db, db.Orders.Where(o => o.Meta.Address.City == "Pécs").ToQueryString());

        Assert.Contains("USING INDEX ix_Orders_Meta_address.city", plan, StringComparison.Ordinal);
        Assert.Equal([1], await db.Orders.Where(o => o.Meta.Address.City == "Pécs").Select(o => o.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Without_the_declaration_the_same_filter_scans()
    {
        // The control: proves the plan assertions above are about the index and not about a planner that would
        // have found another way.
        await using var db = Create<UnindexedOrderContext>();
        TestMigrations.Apply(db);

        var plan = await PlanAsync(db, db.Orders.Where(o => o.Meta.Status == "open").ToQueryString());

        Assert.Contains("SCAN", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adding_the_declaration_to_an_existing_table_is_a_migration_of_its_own()
    {
        await using (var before = Create<UnindexedOrderContext>())
        {
            TestMigrations.Apply(before);
        }

        await using var after = Create<IndexedOrderContext>();
        TestMigrations.Apply(after, Create<UnindexedOrderContext>());

        Assert.Contains("ix_Orders_Meta_status", TestMigrations.Ddl(after), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_the_declaration_drops_the_index()
    {
        await using (var before = Create<IndexedOrderContext>())
        {
            TestMigrations.Apply(before);
        }

        await using var after = Create<UnindexedOrderContext>();
        TestMigrations.Apply(after, Create<IndexedOrderContext>());

        Assert.DoesNotContain("ix_Orders_Meta", TestMigrations.Ddl(after), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_index_survives_a_migration_that_rebuilds_the_table()
    {
        await using (var before = Create<IndexedOrderContext>())
        {
            TestMigrations.Apply(before);
        }

        await using var after = Create<RebuiltIndexedOrderContext>();
        TestMigrations.Apply(after, Create<IndexedOrderContext>());

        var ddl = TestMigrations.Ddl(after);
        Assert.Contains("ix_Orders_Meta_status", ddl, StringComparison.Ordinal);
        Assert.Contains("ix_Orders_Meta_address.city", ddl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_index_built_under_the_older_name_is_renamed_by_the_next_migration()
    {
        await using var db = Create<IndexedOrderContext>();
        TestMigrations.Apply(db);
        await db.Database.ExecuteSqlRawAsync("DROP INDEX \"ix_Orders_Meta_status\"", TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX \"IX_Orders_Meta.Status\" ON \"Orders\" (\"Meta\" ->> 'Status')", TestContext.Current.CancellationToken);
        var model = db.GetService<IDesignTimeModel>().Model;
        var now = (string)model.GetRelationalModel().FindTable("Orders", null)![JsonIndexSpec.AnnotationName]!;
        var upgrade = new AlterTableOperation { Name = "Orders", OldTable = new AlterTableOperation { Name = "Orders" } };
        upgrade[JsonIndexSpec.AnnotationName] = now;
        upgrade.OldTable[JsonIndexSpec.AnnotationName] = now
            .Replace("ix_Orders_Meta_status", "IX_Orders_Meta.Status", StringComparison.Ordinal)
            .Replace("ix_Orders_Meta_address.city", "IX_Orders_Meta.Address.City", StringComparison.Ordinal);

        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate([upgrade], model))
        {
            await db.Database.ExecuteSqlRawAsync(command.CommandText, TestContext.Current.CancellationToken);
        }

        var ddl = TestMigrations.Ddl(db);
        Assert.DoesNotContain("IX_Orders_Meta", ddl, StringComparison.Ordinal);
        Assert.Contains("ix_Orders_Meta_status", ddl, StringComparison.Ordinal);
        Assert.Contains("ix_Orders_Meta_address.city", ddl, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_name_is_cut_to_63_bytes_as_every_other_name_is()
    {
        using var db = Create<LongIndexedOrderContext>();

        TestMigrations.Apply(db);

        var names = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND name LIKE 'ix_%'").ToList();
        Assert.All(names, n => Assert.True(System.Text.Encoding.UTF8.GetByteCount(n) <= 63, n));
        Assert.Contains(names, n => n.StartsWith("ix_OrdersOfAVeryLongTableName", StringComparison.Ordinal));
    }

    [Fact]
    public void A_path_outside_a_json_column_says_so_when_the_migration_is_built()
    {
        using var db = Create<NotJsonContext>();

        var error = Assert.Throws<InvalidOperationException>(() => TestMigrations.Apply(db));

        Assert.Contains("HasJsonIndex(p => p.Meta.Status)", error.Message, StringComparison.Ordinal);
        Assert.Contains("ToJson()", error.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _database.Dispose();

    // Enough rows, spread over enough values, that the planner's statistics — Rask's connections run PRAGMA optimize
    // as they close — say what a real table's would: on a two-row table a scan IS the cheaper plan.
    private async Task<IndexedOrderContext> SeededAsync()
    {
        var db = Create<IndexedOrderContext>();
        TestMigrations.Apply(db);
        string[] cities = ["Győr", "Szeged", "Debrecen", "Miskolc", "Eger", "Sopron", "Tata", "Vác"];
        for (var id = 3; id < 1_003; id++)
        {
            db.Orders.Add(new JsonOrder
            {
                Id = id,
                Meta = new OrderMeta { Status = "s" + (id % 97), Address = new OrderAddress { City = cities[id % cities.Length] + id } },
            });
        }

        db.Orders.AddRange(
            new JsonOrder { Id = 1, Meta = new OrderMeta { Status = "shipped", Address = new OrderAddress { City = "Pécs" } } },
            new JsonOrder { Id = 2, Meta = new OrderMeta { Status = "open", Address = new OrderAddress { City = "Győr" } } });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private TContext Create<TContext>()
        where TContext : DbContext
        => (TContext)Activator.CreateInstance(
            typeof(TContext),
            new DbContextOptionsBuilder<TContext>().UseRaskSqliteAt(_database.ConnectionString).Options)!;

    private static async Task<string> PlanAsync(DbContext db, string queryString)
    {
        // ToQueryString prefixes the SQL with `.param set` lines; bind those for real.
        var lines = queryString.Split('\n');
        var sql = string.Join('\n', lines.Where(l => !l.StartsWith(".param", StringComparison.Ordinal)));

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        foreach (var line in lines.Where(l => l.StartsWith(".param set ", StringComparison.Ordinal)))
        {
            var parts = line[".param set ".Length..].Trim().Split(' ', 2);
            command.Parameters.AddWithValue(parts[0], parts[1].Trim().Trim('\''));
        }

        var plan = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.Add(reader.GetString(3));
        }

        return string.Join('\n', plan);
    }
}
