using Microsoft.EntityFrameworkCore;
using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

/// <summary>
///     <c>database.Arrange&lt;T&gt;(…)</c> and <c>database.ArrangeGone&lt;T&gt;(…)</c>: a test's setup of a state the
///     aggregate would never make, as one statement — every tenant's rows, no stamp, no version, no transaction counted.
/// </summary>
public sealed class ArrangeTests
{
    private static readonly Guid Acme = new("b1a7c0de-5e0a-4b2c-9f4e-0000000000a1");
    private static readonly Guid Globex = new("b1a7c0de-5e0a-4b2c-9f4e-0000000000b2");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Arrange_sets_the_columns_with_no_stamp_no_version_and_no_transaction_and_counts_the_rows()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var acme = Company.Named("Acme");
        await acme.Save(cancellationToken: Ct);
        var before = (await database.Load<Company>(acme.Id, Ct))!;
        using var recorded = Db.Record();

        var changed = await database.Arrange<Company>(c => c.Id == acme.Id, set => set.SetProperty(c => c.Name, "Acme (legacy)"), Ct);

        var after = (await database.Load<Company>(acme.Id, Ct))!;
        Assert.Equal(1, changed);
        Assert.Equal(("Acme (legacy)", before.Version, before.UpdatedAt), (after.Name, after.Version, after.UpdatedAt));
        Assert.Empty(recorded.Transactions);
    }

    [Fact]
    public async Task Arrange_reaches_another_tenants_rows()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var theirs = WharfOperator.Named("globex");
        using (Tenant.Use(Globex))
        {
            await theirs.Save(cancellationToken: Ct);
        }

        using var acme = Tenant.Use(Acme);
        var changed = await database.Arrange<WharfOperator>(o => o.Id == theirs.Id, set => set.SetProperty(o => o.Name, "moved"), Ct);

        await using var fresh = database.Factory.CreateDbContext();
        Assert.Equal(1, changed);
        Assert.Equal("moved", (await fresh.Set<WharfOperator>().IgnoreQueryFilters().SingleAsync(Ct)).Name);
    }

    [Fact]
    public async Task ArrangeGone_removes_a_soft_deleted_aggregates_row_outright_in_any_tenant()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        await Company.Named("Acme").Save(cancellationToken: Ct);
        var theirs = WharfOperator.Named("globex");
        using (Tenant.Use(Globex))
        {
            await theirs.Save(cancellationToken: Ct);
        }

        using var acme = Tenant.Use(Acme);
        var companies = await database.ArrangeGone<Company>(c => c.Name == "Acme", Ct);
        var operators = await database.ArrangeGone<WharfOperator>(o => o.Id == theirs.Id, Ct);

        await using var fresh = database.Factory.CreateDbContext();
        Assert.Equal((1, 1), (companies, operators));
        Assert.Equal(0, await fresh.Set<Company>().IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(0, await fresh.Set<WharfOperator>().IgnoreQueryFilters().CountAsync(Ct));
    }
}
