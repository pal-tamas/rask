using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Rask.Data.Tests.ChildTenants;

namespace Rask.Data.Tests;

/// <summary>
///     An aggregate of thousands of children, saved whole: every changed child is walked to its root by the save's checks,
///     which find each parent by its key in the rows tracked (TrackedPrincipals) — the walk that made Eszaf's supply of
///     thousands of fuelings take minutes to save (2026-11-20). The time is printed, never asserted: a runner's clock
///     proves nothing.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class LargeAggregateSaveTests(ITestOutputHelper output) : IDisposable
{
    private const int Acme = 7;

    private const int Lines = 3000;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-large-aggregate-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task An_aggregate_of_thousands_of_new_children_is_saved_and_each_child_takes_its_roots_tenant()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: TestContext.Current.CancellationToken);
        var supply = Supply.Of("S-big", [.. Enumerable.Range(1, Lines).Select(static i => "goods " + i)]);

        var clock = Stopwatch.StartNew();
        using (Tenant.Use(Acme))
        {
            await GeneratedModelWrites.Create(supply, cancellationToken: TestContext.Current.CancellationToken);
        }

        output.WriteLine($"{Lines} new children saved in {clock.ElapsedMilliseconds} ms");
        using (Tenant.Use(Acme))
        {
            var tenants = await database.Context.Set<SupplyLine>().Select(static l => l.TenantId)
                .ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(Lines, tenants.Count);
            Assert.All(tenants, static tenant => Assert.Equal(Acme, tenant));
        }
    }

    [Fact]
    public async Task Thousands_of_children_changed_in_one_aggregate_are_saved_through_their_root()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: TestContext.Current.CancellationToken);
        using (Tenant.Use(Acme))
        {
            await GeneratedModelWrites.Create(
                Supply.Of("S-big", [.. Enumerable.Range(1, Lines).Select(static i => "goods " + i)]),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        Supply loaded;
        using (Tenant.Use(Acme))
        {
            loaded = await database.Context.Set<Supply>().Include(static s => s.Lines).SingleAsync(TestContext.Current.CancellationToken);
        }

        foreach (var line in loaded.Lines)
        {
            line.Rename(line.Goods + " (repriced)");
        }

        var clock = Stopwatch.StartNew();
        using (Tenant.Use(Acme))
        {
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        output.WriteLine($"{Lines} changed children saved in {clock.ElapsedMilliseconds} ms");
        using (Tenant.Use(Acme))
        {
            var goods = await SupplyLine.Select(static l => l.Goods);
            Assert.Equal(Lines, goods.Count);
            Assert.All(goods, static name => Assert.EndsWith(" (repriced)", name, StringComparison.Ordinal));
        }
    }
}
