using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace Rask.Providers.E2E.Tests;

/// <summary>
/// SQL Server, proved without a server: the tenant in flight is a typed literal in the SQL, so each tenant gets a
/// plan of its own instead of the first tenant's sniffed plan (TA, A11). Execution needs a host that can run the
/// image, which Apple Silicon cannot — <see cref="SqlServerTenantPlanExecutionTests" /> runs on ci/release.
/// </summary>
public sealed class SqlServerTenantPlanTests
{
    private const string Offline = "Server=(local);Database=rask_translation_only;Trusted_Connection=True";

    [Fact]
    public void The_tenant_filter_is_in_the_model_and_names_the_tenant_as_a_guid_literal()
    {
        using var db = SqlServerContext();
        var tenant = Guid.NewGuid();

        string sql;
        using (Tenant.Use(tenant))
        {
            sql = db.Ledgers.ToQueryString();
        }

        Assert.NotNull(db.Model.FindEntityType(typeof(Ledger))!.FindProperty(Columns.TenantId));
        Assert.Contains($"[l].[TenantId] = '{tenant}'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_numbered_tenant_is_an_int_column_compared_with_an_int_literal()
    {
        using var db = SqlServerContext();

        string sql;
        using (Tenant.Use(987654))
        {
            sql = db.Depots.ToQueryString();
        }

        Assert.Equal("int", db.Model.FindEntityType(typeof(Depot))!.FindProperty(Columns.TenantId)!.GetColumnType());
        Assert.Contains("[d].[TenantId] = 987654", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_tenants_alternating_on_one_query_each_get_their_own_literal_in_either_order()
    {
        using var db = SqlServerContext();
        var acme = Guid.NewGuid();
        var globex = Guid.NewGuid();
        var order = new[] { acme, globex, acme, globex, globex, acme };

        var sql = order.Select(tenant =>
        {
            using (Tenant.Use(tenant))
            {
                return db.Ledgers.Where(l => l.Reference != "").ToQueryString();
            }
        }).ToList();

        for (var i = 0; i < order.Length; i++)
        {
            var other = order[i] == acme ? globex : acme;
            Assert.Contains($"'{order[i]}'", sql[i], StringComparison.Ordinal);
            Assert.DoesNotContain($"'{other}'", sql[i], StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Tenants_reading_at_once_never_receive_another_tenants_sql()
    {
        var tenants = Enumerable.Range(1, 16).Select(_ => Guid.NewGuid()).ToList();

        var results = await Task.WhenAll(tenants.Select(tenant => Task.Run(() =>
        {
            using var db = SqlServerContext();
            using (Tenant.Use(tenant))
            {
                return Enumerable.Range(0, 20).Select(_ => db.Ledgers.Where(l => l.Reference != "x").ToQueryString()).ToList();
            }
        }, TestContext.Current.CancellationToken)));

        for (var i = 0; i < tenants.Count; i++)
        {
            Assert.All(results[i], sql => Assert.Contains($"'{tenants[i]}'", sql, StringComparison.Ordinal));
            Assert.All(results[i], sql => Assert.Single(tenants, t => sql.Contains($"'{t}'", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void Across_names_no_tenant_and_no_literal()
    {
        using var db = SqlServerContext();

        string sql;
        using (Tenant.Across())
        {
            sql = db.Ledgers.ToQueryString();
        }

        Assert.DoesNotContain("[TenantId] =", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Forty_tenants_share_one_compiled_query_so_the_cache_does_not_grow_per_tenant()
    {
        using var db = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>()
            .UseRaskSqlServerAt(Offline).EnableServiceProviderCaching(false).Options);
        var cache = (MemoryCache)db.GetService<IMemoryCache>();
        var tenants = Enumerable.Range(1, 40).Select(_ => Guid.NewGuid()).ToList();
        var before = cache.Count;

        foreach (var tenant in tenants.Concat(tenants))
        {
            using (Tenant.Use(tenant))
            {
                _ = db.Ledgers.ToQueryString();
            }
        }

        // EF Core 10 keeps the literal out of the compiled-query key and renders it when the command is built.
        Assert.Equal(1, cache.Count - before);
    }

    [Fact]
    public void On_Postgres_the_tenant_stays_a_parameter()
    {
        using var db = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>()
            .UseNpgsql("Host=localhost;Database=rask_model_only").Options);
        var tenant = Guid.NewGuid();

        string sql;
        using (Tenant.Use(tenant))
        {
            sql = db.Ledgers.ToQueryString();
        }

        Assert.Contains("\"TenantId\" = @", sql, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{tenant}'", sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    private static TenancyDbContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<TenancyDbContext>().UseRaskSqlServerAt(Offline).Options);
}

/// <summary>The same against a real SQL Server: per-tenant plans still return each tenant its own rows only.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerTenantPlanExecutionTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_tenant_plan";

    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    public async ValueTask InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db, _acme, "ACME-1");
        await SeedAsync(db, _globex, "GLOBEX-1", "GLOBEX-2");
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var db = NewContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Two_tenants_alternating_each_count_only_their_own_rows_and_Across_counts_all()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var db = NewContext();
        var counts = new List<int>();

        foreach (var tenant in new Guid?[] { _acme, _globex, null, _globex, _acme, null })
        {
            using var scope = tenant is { } id ? Tenant.Use(id) : Tenant.Across();
            counts.Add(await db.Ledgers.CountAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal([1, 2, 3, 2, 1, 3], counts);
    }

    [Fact]
    public async Task Tenants_reading_at_once_each_read_only_their_own_rows()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        var readers = Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? _acme : _globex).ToList();

        var results = await Task.WhenAll(readers.Select(async tenant =>
        {
            await using var db = NewContext();
            using (Tenant.Use(tenant))
            {
                return await db.Ledgers.Select(l => l.Reference).OrderBy(r => r)
                    .ToListAsync(TestContext.Current.CancellationToken);
            }
        }));

        for (var i = 0; i < readers.Count; i++)
        {
            Assert.Equal(readers[i] == _acme ? ["ACME-1"] : ["GLOBEX-1", "GLOBEX-2"], results[i]);
        }
    }

    private static async Task SeedAsync(TenancyDbContext db, Guid tenant, params string[] references)
    {
        using (Tenant.Use(tenant))
        {
            db.AddRange(references.Select(Ledger.For));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private static TenancyDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TenancyDbContext>()
            .UseRaskSqlServerAt(SqlServer.Database(DatabaseName))
            .AddInterceptors(new AuditingInterceptor(TimeProvider.System))
            .Options);
}
