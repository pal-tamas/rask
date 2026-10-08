using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>A tenant-scoped aggregate: one const, and it never mentions the tenant again.</summary>
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

/// <summary>A tenant-scoped aggregate that READS its tenant, so it declares the column itself.</summary>
public sealed class Vault : Aggregate<Guid>
{
    private Vault() { }

    public const Tenancy Scope = Tenancy.PerTenant;

    public Guid? TenantId { get; private set; }

    public string Code { get; private set; } = "";

    public static Vault For(string code) => new() { Id = Guid.NewGuid(), Code = code };
}

/// <summary>Not partitioned, but it keeps a tenant as data of its own — the shape a queue's rows have.</summary>
public sealed class Haulier : Aggregate<Guid>
{
    private Haulier() { }

    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public static Haulier For(string name, Guid? tenant) => new() { Id = Guid.NewGuid(), Name = name, TenantId = tenant };
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
        // and it carries the column itself, because LedgerEntry.Where(…) is queryable on its own.
        Assert.NotNull(model.FindEntityType(typeof(LedgerEntry))!.FindProperty(Columns.TenantId));

        // A table that said nothing has no such column at all.
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
            Assert.Equal(["ACME-1"], await Ledger.Select(l => l.Reference));
        }

        using (Tenant.Use(_globex))
        {
            Assert.Equal(["GLOBEX-1"], await Ledger.Select(l => l.Reference));
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
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.Equal(_acme, TenantOf(database, ledger));
            Assert.Equal(_acme, TenantOf(database, ledger.Entries.Single()));
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
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Ledger.All);
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
                (await Ledger.Select(l => l.Reference)).Order(StringComparer.Ordinal));
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
                await Ledger.IgnoreQueryFilters().Select(l => l.Reference));
        }
    }

    [Fact]
    public async Task A_row_cannot_move_to_another_tenant()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(database, _acme, "ACME-1");

        using (Tenant.Use(_acme))
        {
            var ledger = await database.Context.Set<Ledger>().SingleAsync(l => l.Id == id, cancellationToken: TestContext.Current.CancellationToken);
            database.Context.Entry(ledger).Property(Columns.TenantId).CurrentValue = _globex;

            // The query filter already stops you LOADING another tenant's row. This catches what it cannot:
            // a row whose TenantId is reassigned in code, saved into a tenant it never belonged to.
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_read_inside_a_session_scope_sees_that_sessions_tenant()
    {
        await using var database = await StartDatabaseAsync();

        await SaveAsync(database, _acme, "ACME-1");
        await SaveAsync(database, _globex, "GLOBEX-1");

        // What the host does around a live session's work: the session's own services become ambient, and a
        // read — a static call, outside any DI scope — resolves the tenant of the user that session is for.
        using (Db.UseScope(ScopeFor(_acme)))
        {
            Assert.Equal(["ACME-1"], await Ledger.Select(l => l.Reference));
        }

        using (Db.UseScope(ScopeFor(_globex)))
        {
            Assert.Equal(["GLOBEX-1"], await Ledger.Select(l => l.Reference));
        }
    }

    [Fact]
    public async Task A_session_scope_does_not_outlive_itself()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(database, _acme, "ACME-1");

        using (Db.UseScope(ScopeFor(_acme)))
        {
            Assert.Single(await Ledger.All);
        }

        // The failure this guards against is the worst one available: a scope that leaked past the work it
        // bracketed would hand the NEXT session the previous user's tenant.
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Ledger.All);
    }

    [Fact]
    public async Task A_nested_scope_restores_exactly_what_it_replaced()
    {
        await using var database = await StartDatabaseAsync();

        await SaveAsync(database, _acme, "ACME-1");
        await SaveAsync(database, _globex, "GLOBEX-1");

        // A handler's dispatch renders, so the scope is entered inside itself.
        using (Db.UseScope(ScopeFor(_acme)))
        {
            using (Db.UseScope(ScopeFor(_globex)))
            {
                Assert.Equal(["GLOBEX-1"], await Ledger.Select(l => l.Reference));
            }

            Assert.Equal(["ACME-1"], await Ledger.Select(l => l.Reference));
        }
    }

    [Fact]
    public async Task An_explicit_scope_beats_the_signed_in_user()
    {
        await using var database = await StartDatabaseAsync();

        await SaveAsync(database, _acme, "ACME-1");
        await SaveAsync(database, _globex, "GLOBEX-1");

        // A background job runs for the tenant its own row recorded, not for whoever enqueued it.
        using (Db.UseScope(ScopeFor(_acme)))
        using (Tenant.Use(_globex))
        {
            Assert.Equal(["GLOBEX-1"], await Ledger.Select(l => l.Reference));
        }
    }

    [Fact]
    public async Task An_insert_takes_the_signed_in_users_tenant_with_no_scope_opened()
    {
        await using var database = await StartDatabaseAsync();

        // The shape of every write a signed-in page makes: the session is ambient, nobody opened Tenant.Use.
        // Reads already filtered by the principal's tenant; the insert stamp must agree with them, or the
        // page can list its tenant's rows and cannot add one.
        using (Db.UseScope(ScopeFor(_acme)))
        {
            var ledger = Ledger.For("ACME-1");
            database.Context.Add(ledger);
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.Equal(_acme, TenantOf(database, ledger));
            Assert.Equal(["ACME-1"], await Ledger.Select(l => l.Reference));
        }
    }

    [Fact]
    public async Task A_table_that_does_not_declare_its_tenant_keeps_it_as_a_shadow_column()
    {
        await using var database = await StartDatabaseAsync();

        var column = database.Context.Model.FindEntityType(typeof(Ledger))!.FindProperty(Columns.TenantId)!;

        Assert.True(column.IsShadowProperty());
        Assert.Equal(typeof(Guid?), column.ClrType);
        Assert.Equal("TenantId", column.GetColumnName());
    }

    [Fact]
    public async Task A_table_that_declares_its_tenant_maps_that_property_to_the_same_column()
    {
        await using var database = await StartDatabaseAsync();

        var column = database.Context.Model.FindEntityType(typeof(Vault))!.FindProperty(Columns.TenantId)!;

        Assert.False(column.IsShadowProperty());
        Assert.Equal(typeof(Guid?), column.ClrType);
        Assert.Equal("TenantId", column.GetColumnName());
    }

    [Fact]
    public async Task A_declared_tenant_reads_the_stamp_after_the_save_and_after_a_load()
    {
        await using var database = await StartDatabaseAsync();
        var vault = Vault.For("V-1");

        using (Tenant.Use(_acme))
        {
            database.Context.Add(vault);
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
            database.Context.ChangeTracker.Clear();
            var loaded = await database.Context.Set<Vault>().SingleAsync(TestContext.Current.CancellationToken);

            Assert.Equal(_acme, vault.TenantId);
            Assert.Equal(_acme, loaded.TenantId);
        }
    }

    [Fact]
    public async Task A_table_that_declares_its_tenant_is_filtered_like_any_other()
    {
        await using var database = await StartDatabaseAsync();

        foreach (var (tenant, code) in new[] { (_acme, "V-ACME"), (_globex, "V-GLOBEX") })
        {
            using (Tenant.Use(tenant))
            {
                database.Context.Add(Vault.For(code));
                await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        using (Tenant.Use(_globex))
        {
            Assert.Equal(["V-GLOBEX"], await Vault.Select(v => v.Code));
        }
    }

    [Fact]
    public async Task A_row_that_declares_its_tenant_cannot_move_to_another_tenant_either()
    {
        await using var database = await StartDatabaseAsync();
        var vault = Vault.For("V-1");

        using (Tenant.Use(_acme))
        {
            database.Context.Add(vault);
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
            database.Context.Entry(vault).Property(v => v.TenantId).CurrentValue = _globex;

            await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_tenant_declared_on_a_table_that_is_not_partitioned_is_an_ordinary_column()
    {
        await using var database = await StartDatabaseAsync();

        // No tenant in flight at all: nothing stamps the row, nothing demands a tenant, nothing filters it.
        database.Context.AddRange(Haulier.For("host", tenant: null), Haulier.For("acme", _acme));
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var hauliers = await Haulier.Select(c => c.Name);

        Assert.Equal(["acme", "host"], hauliers.Order(StringComparer.Ordinal));
        Assert.NotNull(database.Context.Model.FindEntityType(typeof(Haulier))!.FindProperty(Columns.TenantId));
        Assert.Empty(database.Context.Model.FindEntityType(typeof(Haulier))!.GetDeclaredQueryFilters());
    }

    // The stamp of a row that does not declare its tenant: the column is a shadow one, read through the entry.
    private static object? TenantOf(TestDatabase database, object row) =>
        database.Context.Entry(row).Property(Columns.TenantId).CurrentValue;

    private static IServiceProvider ScopeFor(Guid tenant) => new StubScope(new StubTenantSource(tenant));

    private sealed class StubTenantSource(Guid tenant) : IPrincipalSource
    {
        public ClaimsPrincipal? Current { get; } =
            new(new ClaimsIdentity([new Claim(Tenant.ClaimType, tenant.ToString())], "Test"));
    }

    private sealed class StubScope(IPrincipalSource source) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IPrincipalSource) ? source : null;
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
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
