using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data.Tests;

// Tenant aggregates of each key type. None says ITenant — one assembly may say it once, and this one keeps tenant-scoped
// tables of every column type — so each test names its tenant to CurrentRows exactly as the generated code would.
public sealed class NumberShop : Aggregate<int, Model, Deletable>
{
    private NumberShop() { }

    public string Name { get; private set; } = "";

    public static NumberShop Open(int id, string name) => new() { Id = id, Name = name };

    public void Rename(string name) => Name = name;
}

public sealed class BigShop : Aggregate<long>
{
    private BigShop() { }

    public string Name { get; private set; } = "";

    public static BigShop Open(long id, string name) => new() { Id = id, Name = name };
}

public sealed class GuidShop : Aggregate<Guid>
{
    private GuidShop() { }

    public string Name { get; private set; } = "";

    public static GuidShop Open(Guid id, string name) => new() { Id = id, Name = name };
}

public sealed class TextShop : Aggregate<string>
{
    private TextShop() { }

    public string Name { get; private set; } = "";

    public static TextShop Open(string id, string name) => new() { Id = id, Name = name };
}

/// <summary>
/// <c>Current.Tenant</c> is the app's own tenant row: read by the key in flight, held per key, dropped on a save, and
/// never another tenant's, nobody's, or a row that is gone.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class CurrentTenantRowTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-current-tenant-{Guid.NewGuid():N}.db");
    private readonly QueryCounter _queries = new();

    public void Dispose()
    {
        CurrentRows.Reset();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task An_int_tenant_reads_its_own_row()
    {
        await using var database = await StartAsync();
        await SeedAsync(database, NumberShop.Open(7, "Acme"), NumberShop.Open(8, "Globex"));
        UseTenant<NumberShop, NumberShopRead, int>(static key => row => row.Id == key, static row => row.Id);

        using (Tenant.Use(7))
        {
            var row = CurrentRows.Tenant<NumberShopRead>();

            Assert.Equal(7, row!.Id);
            Assert.Equal("Acme", row.Name);
        }
    }

    [Fact]
    public async Task A_long_tenant_reads_its_own_row()
    {
        await using var database = await StartAsync();
        await SeedAsync(database, BigShop.Open(5_000_000_000, "Acme"), BigShop.Open(5_000_000_001, "Globex"));
        UseTenant<BigShop, BigShopRead, long>(static key => row => row.Id == key, static row => row.Id);

        using (Tenant.Use(5_000_000_001))
        {
            Assert.Equal("Globex", CurrentRows.RequiredTenant<BigShopRead>().Name);
        }
    }

    [Fact]
    public async Task A_Guid_tenant_reads_its_own_row()
    {
        var acme = Guid.NewGuid();
        await using var database = await StartAsync();
        await SeedAsync(database, GuidShop.Open(acme, "Acme"), GuidShop.Open(Guid.NewGuid(), "Globex"));
        UseTenant<GuidShop, GuidShopRead, Guid>(static key => row => row.Id == key, static row => row.Id);

        using (Tenant.Use(acme))
        {
            Assert.Equal(acme, CurrentRows.Tenant<GuidShopRead>()!.Id);
        }
    }

    [Fact]
    public async Task A_string_tenant_reads_the_row_whose_text_is_its_Guid()
    {
        var acme = Guid.NewGuid();
        await using var database = await StartAsync();
        await SeedAsync(database, TextShop.Open(acme.ToString("D"), "Acme"), TextShop.Open(Guid.NewGuid().ToString("D"), "Globex"));
        UseTenant<TextShop, TextShopRead, string>(static key => row => row.Id == key, static row => row.Id);

        using (Tenant.Use(acme))
        {
            Assert.Equal("Acme", CurrentRows.Tenant<TextShopRead>()!.Name);
        }
    }

    [Fact]
    public async Task Across_and_None_have_no_tenant_row()
    {
        await using var database = await StartAsync();
        await SeedAsync(database, NumberShop.Open(7, "Acme"));
        UseTenant<NumberShop, NumberShopRead, int>(static key => row => row.Id == key, static row => row.Id);
        _queries.Reset();

        using (Tenant.Use(7))
        {
            using (Tenant.Across())
            {
                Assert.Null(CurrentRows.Tenant<NumberShopRead>());
                Assert.Throws<InvalidOperationException>(() => CurrentRows.RequiredTenant<NumberShopRead>());
            }

            using (Tenant.None())
            {
                Assert.Null(CurrentRows.Tenant<NumberShopRead>());
            }
        }

        Assert.Equal(0, _queries.Total);
    }

    [Fact]
    public async Task A_nested_Use_never_sees_the_outer_tenants_row()
    {
        await using var database = await StartAsync();
        await SeedAsync(database, NumberShop.Open(7, "Acme"), NumberShop.Open(8, "Globex"));
        UseTenant<NumberShop, NumberShopRead, int>(static key => row => row.Id == key, static row => row.Id);

        string outer, inner, after;
        using (Tenant.Use(7))
        {
            outer = CurrentRows.RequiredTenant<NumberShopRead>().Name;
            using (Tenant.Use(8))
            {
                inner = CurrentRows.RequiredTenant<NumberShopRead>().Name;
            }

            after = CurrentRows.RequiredTenant<NumberShopRead>().Name;
        }

        Assert.Equal(("Acme", "Globex", "Acme"), (outer, inner, after));
    }

    [Fact]
    public async Task A_miss_is_one_synchronous_query_and_then_a_lookup()
    {
        await using var database = await StartAsync();
        await SeedAsync(database, NumberShop.Open(7, "Acme"));
        UseTenant<NumberShop, NumberShopRead, int>(static key => row => row.Id == key, static row => row.Id);
        _queries.Reset();

        using (Tenant.Use(7))
        {
            _ = CurrentRows.Tenant<NumberShopRead>();
            _ = CurrentRows.Tenant<NumberShopRead>();
            _ = CurrentRows.RequiredTenant<NumberShopRead>();
        }

        Assert.Equal((1, 0), (_queries.Sync, _queries.Async));
    }

    [Fact]
    public async Task Work_the_framework_starts_reads_the_row_ahead_so_the_handler_never_queries()
    {
        await using var database = await StartAsync();
        await SeedAsync(database, NumberShop.Open(7, "Acme"));
        UseTenant<NumberShop, NumberShopRead, int>(static key => row => row.Id == key, static row => row.Id);
        _queries.Reset();

        // What a job processor does before its handler: the tenant its row recorded, then the read ahead.
        string name;
        using (Tenant.Use(7))
        {
            await CurrentRows.Prime(TestContext.Current.CancellationToken);
            var async = _queries.Async;
            name = CurrentRows.RequiredTenant<NumberShopRead>().Name;
            Assert.Equal(1, async);
        }

        Assert.Equal("Acme", name);
        Assert.Equal((0, 1), (_queries.Sync, _queries.Async));
    }

    [Fact]
    public async Task A_tenant_whose_row_is_gone_throws_and_nothing_is_kept()
    {
        await using var database = await StartAsync();
        UseTenant<NumberShop, NumberShopRead, int>(static key => row => row.Id == key, static row => row.Id);
        _queries.Reset();

        using (Tenant.Use(9))
        {
            Assert.Throws<MissingTenantException>(() => CurrentRows.Tenant<NumberShopRead>());
            Assert.Throws<MissingTenantException>(() => CurrentRows.Tenant<NumberShopRead>());
        }

        Assert.Equal(2, _queries.Sync);
    }

    [Fact]
    public async Task A_row_keyed_otherwise_than_asked_throws_and_is_not_kept()
    {
        await using var database = await StartAsync();
        await SeedAsync(database, NumberShop.Open(7, "Acme"), NumberShop.Open(8, "Globex"));

        // A lookup that finds the wrong row — a converter on the key, a bug — must not hand it over as this tenant's.
        UseTenant<NumberShop, NumberShopRead, int>(static key => row => row.Id == key + 1, static row => row.Id);
        _queries.Reset();

        using (Tenant.Use(7))
        {
            Assert.Throws<InvalidOperationException>(() => CurrentRows.Tenant<NumberShopRead>());
            Assert.Throws<InvalidOperationException>(() => CurrentRows.Tenant<NumberShopRead>());
        }

        Assert.Equal(2, _queries.Sync);
    }

    [Fact]
    public async Task A_test_database_save_of_the_tenant_shows_the_new_row_at_once()
    {
        await using var database = await StartAsync();
        await SeedAsync(database, NumberShop.Open(7, "Acme"));
        UseTenant<NumberShop, NumberShopRead, int>(static key => row => row.Id == key, static row => row.Id);

        string before, after;
        using (Tenant.Use(7))
        {
            before = CurrentRows.RequiredTenant<NumberShopRead>().Name;

            var shop = (await NumberShop.Find(7, TestContext.Current.CancellationToken))!;
            shop.Rename("Acme Ltd");
            await shop.Save(cancellationToken: TestContext.Current.CancellationToken);

            after = CurrentRows.RequiredTenant<NumberShopRead>().Name;
        }

        Assert.Equal(("Acme", "Acme Ltd"), (before, after));
    }

    [Fact]
    public async Task A_deleted_tenant_throws_once_its_delete_is_saved()
    {
        await using var database = await StartAsync();
        await SeedAsync(database, NumberShop.Open(7, "Acme"));
        UseTenant<NumberShop, NumberShopRead, int>(static key => row => row.Id == key, static row => row.Id);

        using (Tenant.Use(7))
        {
            _ = CurrentRows.RequiredTenant<NumberShopRead>();
            database.Context.Remove((await database.Context.Set<NumberShop>().SingleAsync(s => s.Id == 7, TestContext.Current.CancellationToken))!);
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.Throws<MissingTenantException>(() => CurrentRows.RequiredTenant<NumberShopRead>());
        }
    }

    private static void UseTenant<TAggregate, [DynamicallyAccessedMembers(DataTrimming.Entity)] TRead, TKey>(
        Func<TKey, System.Linq.Expressions.Expression<Func<TRead, bool>>> byKey, Func<TRead, TKey> keyOf)
        where TRead : class, IReadModel =>
        CurrentRows.Use(new CurrentRow<TRead, TKey>(CurrentRowKind.Tenant, typeof(TAggregate), byKey, keyOf));

    private static async Task SeedAsync(TestDatabase database, params object[] rows)
    {
        database.Context.AddRange(rows);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.Context.ChangeTracker.Clear();
    }

    private Task<TestDatabase> StartAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}").AddInterceptors(_queries));

    private sealed class QueryCounter : DbCommandInterceptor
    {
        private int _sync;
        private int _async;

        public int Sync => Volatile.Read(ref _sync);

        public int Async => Volatile.Read(ref _async);

        public int Total => Sync + Async;

        public void Reset()
        {
            Volatile.Write(ref _sync, 0);
            Volatile.Write(ref _async, 0);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _sync);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _async);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
