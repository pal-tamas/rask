using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

public sealed record SkiffSize(int Width, int Height);

/// <summary>A tenant-scoped aggregate holding a multi-value value object (a complex type).</summary>
public sealed class Skiff : Aggregate<int>, ITimestamps, IVersioned, IFormModel, IPerTenant
{
    private Skiff() { }

    public SkiffSize Size { get; private set; } = new(0, 0);

    public static Skiff Of(int width, int height) => new() { Size = new SkiffSize(width, height) };
}

/// <summary>
/// The tenant filter folds <c>Across</c> into the query (<c>EF.Constant</c>), so one LINQ shape has two cached
/// plans. These prove the two never answer for each other, in either order and under interleaving threads, that
/// the tenant itself is still a parameter, and that the filter still reaches every way a table is read.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class TenantFilterPlanTests : IDisposable
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-tenant-plan-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task The_same_query_run_across_first_then_in_a_tenant_returns_each_its_own_rows()
    {
        await using var database = await SeedAsync();

        var across = await AcrossAsync();
        var acme = await InAsync(Acme);
        var globex = await InAsync(Globex);

        Assert.Equal(["Budapest", "Nowhere", "Szeged", "Vienna"], across);
        Assert.Equal(["Budapest", "Szeged"], acme);
        Assert.Equal(["Vienna"], globex);
    }

    [Fact]
    public async Task The_same_query_run_in_a_tenant_first_then_across_returns_each_its_own_rows()
    {
        await using var database = await SeedAsync();

        var acme = await InAsync(Acme);
        var across = await AcrossAsync();
        var globex = await InAsync(Globex);
        var acmeAgain = await InAsync(Acme);

        Assert.Equal(["Budapest", "Szeged"], acme);
        Assert.Equal(["Budapest", "Nowhere", "Szeged", "Vienna"], across);
        Assert.Equal(["Vienna"], globex);
        Assert.Equal(acme, acmeAgain);
    }

    [Fact]
    public async Task Two_threads_one_across_and_one_in_a_tenant_never_see_each_others_rows()
    {
        await using var database = await SeedAsync();
        const int Rounds = 200;

        var across = Task.Run(async () =>
        {
            for (var i = 0; i < Rounds; i++)
            {
                Assert.Equal(["Budapest", "Nowhere", "Szeged", "Vienna"], await AcrossAsync());
                await Task.Yield();
            }
        }, TestContext.Current.CancellationToken);
        var tenant = Task.Run(async () =>
        {
            for (var i = 0; i < Rounds; i++)
            {
                Assert.Equal(["Budapest", "Szeged"], await InAsync(Acme));
                await Task.Yield();
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(across, tenant);
    }

    [Fact]
    public async Task The_tenant_stays_a_parameter_and_two_tenants_share_one_sql_text()
    {
        await using var database = await SeedAsync();

        string acme, globex;
        using (Tenant.Use(987654))
        {
            acme = database.Context.Set<Destination>().ToQueryString();
        }

        using (Tenant.Use(123456))
        {
            globex = database.Context.Set<Destination>().ToQueryString();
        }

        var acmeSql = acme.Split("\n\n")[^1];
        Assert.DoesNotContain("987654", acmeSql, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", globex.Split("\n\n")[^1], StringComparison.Ordinal);
        Assert.Equal(acmeSql, globex.Split("\n\n")[^1]);
    }

    [Fact]
    public async Task A_row_of_no_tenant_is_excluded_outside_across()
    {
        await using var database = await SeedAsync();

        var inTenant = await InAsync(Acme);

        Assert.DoesNotContain("Nowhere", inTenant);
        Assert.Contains("Nowhere", await AcrossAsync());
    }

    [Fact]
    public async Task A_resolver_that_names_no_tenant_reads_nothing_and_across_still_reads_everything()
    {
        await using var database = await SeedAsync();
        await using var app = new ServiceCollection().AddRaskTenant(_ => (int?)null).BuildServiceProvider(validateScopes: true);

        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            Assert.Empty(await Destination.Select(d => d.Name));
            using (Tenant.Across())
            {
                Assert.Equal(4, (await Destination.Select(d => d.Name)).Count);
            }

            Assert.Empty(await Destination.Select(d => d.Name));
        }
    }

    [Fact]
    public async Task An_included_child_collection_is_filtered_and_has_no_catch_all()
    {
        await using var database = await StartAsync();
        using (Tenant.Use(Acme))
        {
            var ledger = Ledger.For("ACME");
            ledger.Add("rent");
            await ledger.Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        using (Tenant.Use(Globex))
        {
            var ledger = Ledger.For("GLOBEX");
            ledger.Add("fuel");
            ledger.Add("tolls");
            await ledger.Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        string sql;
        List<Ledger> acme, across;
        using (Tenant.Use(Acme))
        {
            var query = database.Context.Set<Ledger>().Include(l => l.Entries).AsNoTracking();
            sql = query.ToQueryString();
            acme = await query.ToListAsync(TestContext.Current.CancellationToken);
        }

        using (Tenant.Across())
        {
            across = await database.Context.Set<Ledger>().Include(l => l.Entries).AsNoTracking()
                .OrderBy(l => l.Reference).ToListAsync(TestContext.Current.CancellationToken);
        }

        Assert.DoesNotContain(" OR ", sql, StringComparison.Ordinal);
        Assert.Equal(["ACME"], acme.Select(l => l.Reference));
        Assert.Equal(["rent"], acme.Single().Entries.Select(e => e.Description));
        Assert.Equal([1, 2], across.Select(l => l.Entries.Count));
    }

    [Fact]
    public async Task A_complex_type_is_read_with_its_owner_and_only_for_the_tenant()
    {
        await using var database = await StartAsync();
        using (Tenant.Use(Acme))
        {
            await Skiff.Of(3, 4).Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        using (Tenant.Use(Globex))
        {
            await Skiff.Of(9, 9).Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        List<Skiff> acme, across;
        using (Tenant.Use(Acme))
        {
            acme = await database.Context.Set<Skiff>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        }

        using (Tenant.Across())
        {
            across = await database.Context.Set<Skiff>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal([new SkiffSize(3, 4)], acme.Select(c => c.Size));
        Assert.Equal(2, across.Count);
    }

    [Fact]
    public async Task The_read_face_filters_alternately_across_and_in_a_tenant()
    {
        await using var database = await SeedAsync();
        await using var read = database.OpenRead();

        var counts = new List<int>();
        foreach (var tenant in new int?[] { Acme, null, Globex, null, Acme })
        {
            using var scope = tenant is { } number ? Tenant.Use(number) : Tenant.Across();
            counts.Add(await read.Set<DestinationRead>().CountAsync(TestContext.Current.CancellationToken));
        }

        string sql;
        using (Tenant.Use(Acme))
        {
            sql = read.Set<DestinationRead>().ToQueryString();
        }

        Assert.Equal([2, 4, 1, 4, 2], counts);
        Assert.DoesNotContain(" OR ", sql, StringComparison.Ordinal);
    }

    private static async Task<List<string>> InAsync(int tenant)
    {
        using (Tenant.Use(tenant))
        {
            return (await Destination.Select(d => d.Name)).Order().ToList();
        }
    }

    private static async Task<List<string>> AcrossAsync()
    {
        using (Tenant.Across())
        {
            return (await Destination.Select(d => d.Name)).Order().ToList();
        }
    }

    // Budapest and Szeged for Acme, Vienna for Globex, and a row that belongs to no tenant.
    private async Task<TestDatabase> SeedAsync()
    {
        var database = await StartAsync();
        using (Tenant.Use(Acme))
        {
            await Destination.Named("Budapest").Save(cancellationToken: TestContext.Current.CancellationToken);
            await Destination.Named("Szeged").Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        using (Tenant.Use(Globex))
        {
            await Destination.Named("Vienna").Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        await database.Context.Database.ExecuteSqlAsync(
            $"INSERT INTO Destinations (Name, TenantId) VALUES ('Nowhere', NULL)", TestContext.Current.CancellationToken);

        return database;
    }

    private Task<TestDatabase> StartAsync() => TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
