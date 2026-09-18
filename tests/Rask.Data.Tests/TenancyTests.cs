using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>A tenant-scoped aggregate: one const, and nothing else is configured.</summary>
public sealed class Ledger : Aggregate<Guid>
{
    private readonly List<LedgerEntry> _entries = [];

    private Ledger() { }

    public const Tenancy Scope = Tenancy.PerTenant;

    public string Reference { get; private set; } = "";

    public IReadOnlyCollection<LedgerEntry> Entries => _entries;

    public static Ledger For(string reference) => new() { Id = Guid.NewGuid(), Reference = reference };

    public LedgerEntry Add(string description)
    {
        var entry = LedgerEntry.For(description);
        _entries.Add(entry);
        return entry;
    }
}

/// <summary>A child. It declares nothing and takes its root's tenancy.</summary>
public sealed class LedgerEntry : Entity<Guid>
{
    private LedgerEntry() { }

    public string Description { get; private set; } = "";

    internal static LedgerEntry For(string description) =>
        new() { Id = Guid.NewGuid(), Description = description };
}

/// <summary>An aggregate that says nothing, so it is one table for everybody.</summary>
public sealed class RateCard : Aggregate<Guid>
{
    private RateCard() { }

    public string Country { get; private set; } = "";

    public static RateCard For(string country) => new() { Id = Guid.NewGuid(), Country = country };
}

/// <summary>
/// A <c>Scope</c> const partitions a table by tenant: a <c>TenantId</c> column, a query filter no read can
/// compose away, and a stamp on insert.
/// </summary>
/// <remarks>
/// The filter reaches the current tenant through an instance member of the context, and that is load-bearing
/// rather than a style choice. A query filter is compiled into the CACHED model, so a static read is
/// evaluated once and inlined into the SQL as a literal — measured before this was written: the second tenant
/// to query saw the first tenant's rows, with the first tenant's id sitting in the SQL as a constant.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class TenancyTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-tenancy-{Guid.NewGuid():N}.db");
    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Only_a_table_that_asked_carries_a_tenant_and_a_child_takes_its_roots_answer()
    {
        await using var database = await StartDatabaseAsync();
        var model = database.Context.Model;

        Assert.NotNull(model.FindEntityType(typeof(Ledger))!.FindProperty(Columns.TenantId));

        // The child never says anything: it is part of the aggregate, so it belongs to the root's tenant —
        // and it carries the column itself, because LedgerEntry.Read is queryable on its own.
        Assert.NotNull(model.FindEntityType(typeof(LedgerEntry))!.FindProperty(Columns.TenantId));

        // Ignored, not merely unmapped, on a table that said nothing.
        Assert.Null(model.FindEntityType(typeof(RateCard))!.FindProperty(Columns.TenantId));
    }

    [Fact]
    public async Task A_tenant_sees_only_its_own_rows()
    {
        await using var database = await StartDatabaseAsync();

        await SaveAsync(database, _acme, "ACME-1");
        await SaveAsync(database, _globex, "GLOBEX-1");

        using (Tenant.Use(_acme))
        {
            Assert.Equal(["ACME-1"], await Ledger.Read.Select(l => l.Reference).ToListAsync());
        }

        using (Tenant.Use(_globex))
        {
            Assert.Equal(["GLOBEX-1"], await Ledger.Read.Select(l => l.Reference).ToListAsync());
        }
    }

    [Fact]
    public async Task An_insert_is_stamped_with_the_tenant_in_flight_root_and_child()
    {
        await using var database = await StartDatabaseAsync();

        using (Tenant.Use(_acme))
        {
            var ledger = Ledger.For("ACME-1");
            ledger.Add("widgets");
            database.Context.Add(ledger);
            await database.Context.SaveChangesAsync();

            Assert.Equal(_acme, ledger.TenantId);
            Assert.Equal(_acme, ledger.Entries.Single().TenantId);
        }
    }

    [Fact]
    public async Task A_read_with_no_tenant_throws_rather_than_returning_nothing()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(database, _acme, "ACME-1");

        // Returning nothing would be safe and indistinguishable from an empty database, which is the failure
        // that keeps costing this codebase; returning everything would be the leak.
        using (Tenant.None())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Ledger.Read.ToListAsync());
        }
    }

    [Fact]
    public async Task Across_sees_every_tenant()
    {
        await using var database = await StartDatabaseAsync();

        await SaveAsync(database, _acme, "ACME-1");
        await SaveAsync(database, _globex, "GLOBEX-1");

        using (Tenant.Across())
        {
            Assert.Equal(
                ["ACME-1", "GLOBEX-1"],
                (await Ledger.Read.Select(l => l.Reference).ToListAsync()).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task IgnoreQueryFilters_includes_soft_deleted_rows_but_never_crosses_tenants()
    {
        await using var database = await StartDatabaseAsync();

        await SaveAsync(database, _acme, "ACME-1");
        await SaveAsync(database, _globex, "GLOBEX-1");

        // The named filters are what makes this possible: lifting "SoftDelete" leaves "Tenant" standing, so
        // an existing IgnoreQueryFilters() call never quietly becomes a cross-tenant read.
        using (Tenant.Use(_acme))
        {
            Assert.Equal(
                ["ACME-1"],
                await Ledger.Read.IgnoreQueryFilters().Select(l => l.Reference).ToListAsync());
        }
    }

    [Fact]
    public async Task A_row_cannot_move_to_another_tenant()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(database, _acme, "ACME-1");

        using (Tenant.Use(_acme))
        {
            var ledger = await database.Context.Set<Ledger>().SingleAsync(l => l.Id == id);
            database.Context.Entry(ledger).Property(Columns.TenantId).CurrentValue = _globex;

            // The query filter already stops you LOADING another tenant's row. This catches what it cannot:
            // a row whose TenantId is reassigned in code, saved into a tenant it never belonged to.
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.SaveChangesAsync());
        }
    }

    private async Task<Guid> SaveAsync(TestDatabase database, Guid tenant, string reference)
    {
        using (Tenant.Use(tenant))
        {
            var ledger = Ledger.For(reference);
            database.Context.Add(ledger);
            await database.Context.SaveChangesAsync();
            return ledger.Id;
        }
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));
}
