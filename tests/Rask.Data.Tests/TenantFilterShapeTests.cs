using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
/// The tenant filter must stay a plain <c>TenantId = @p</c> whenever a tenant is in flight: a catch-all
/// <c>@flag = 1 OR TenantId = @p</c> cannot be answered from an index on SQL Server, so a read of a large
/// table scans it (measured: 600 ms against 3 ms over 591 136 rows).
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class TenantFilterShapeTests : IDisposable
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-tenant-shape-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_tenant_in_flight_makes_the_filter_a_plain_equality_with_no_or()
    {
        await using var database = await StartDatabaseAsync();

        string sql;
        using (Tenant.Use(Acme))
        {
            sql = database.Context.Set<Destination>().ToQueryString();
        }

        var where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        Assert.Contains("\"TenantId\" = @", where, StringComparison.Ordinal);
        Assert.DoesNotContain(" OR ", where, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Across_reads_every_tenant_and_names_no_tenant_in_the_sql()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "Budapest");
        await SaveAsync(Globex, "Vienna");

        string sql;
        List<string> names;
        using (Tenant.Across())
        {
            sql = database.Context.Set<Destination>().ToQueryString();
            names = await database.Context.Set<Destination>().Select(d => d.Name).ToListAsync(TestContext.Current.CancellationToken);
        }

        Assert.DoesNotContain("\"TenantId\" =", sql, StringComparison.Ordinal);
        Assert.Equal(["Budapest", "Vienna"], names.Order());
    }

    [Fact]
    public async Task Alternating_across_and_a_tenant_on_one_query_shape_keeps_each_answer()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "Budapest");
        await SaveAsync(Globex, "Vienna");
        await SaveAsync(Globex, "Graz");
        var counts = new List<int>();

        foreach (var tenant in new int?[] { Acme, null, Globex, null, Acme })
        {
            using var scope = tenant is { } number ? Tenant.Use(number) : Tenant.Across();
            counts.Add(await database.Context.Set<Destination>().CountAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal([1, 3, 2, 3, 1], counts);
    }

    [Fact]
    public async Task No_tenant_in_flight_still_refuses_the_read()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.Context.Set<Destination>().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_write_still_stamps_the_tenant_in_flight()
    {
        await using var database = await StartDatabaseAsync();

        await SaveAsync(Acme, "Budapest");

        var stored = await database.Context.Database
            .SqlQuery<int?>($"SELECT TenantId AS Value FROM Destinations")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([Acme], stored);
    }

    private static async Task SaveAsync(int tenant, string name)
    {
        using (Tenant.Use(tenant))
        {
            await Destination.Named(name).Save(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
