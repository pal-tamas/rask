using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
///     A dry run tells what only the store would refuse — a name already taken, a reference to no row of the tenant's, a
///     row still pointing at what a delete removes — asked as queries, with nothing written.
/// </summary>
public sealed class DryRunStoreTests : IDisposable
{
    private static readonly Guid Acme = new("9d4f2e5a-4d9f-4a1b-8e3d-0000000000a1");
    private static readonly Guid Globex = new("9d4f2e5a-4d9f-4a1b-8e3d-0000000000b2");

    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_name_another_row_of_the_tenant_holds_is_told_under_its_field()
    {
        await using var database = await Start();
        using var acme = Tenant.Use(Acme);
        await Slipway.Open("North", Token);
        var south = await Slipway.Open("South", Token);

        var failures = await Slipway.DryRunRename(south, 0, "North", Token);

        var failure = Assert.Single(failures);
        Assert.Equal(Slipway.NameTaken, failure.Message);
        Assert.Contains("Name", failure.Fields);
        Assert.Equal("South", (await Slipway.First(b => b.Id == south, Token)).Name);
    }

    [Fact]
    public async Task A_name_only_another_tenant_holds_is_free()
    {
        await using var database = await Start();
        using (Tenant.Use(Globex))
        {
            await Slipway.Open("North", Token);
        }

        using var acme = Tenant.Use(Acme);
        var south = await Slipway.Open("South", Token);

        var failures = await Slipway.DryRunRename(south, 0, "North", Token);

        Assert.Empty(failures);
    }

    [Fact]
    public async Task A_name_the_operation_leaves_as_it_was_asks_nothing_of_its_rule()
    {
        await using var database = await Start();
        using var acme = Tenant.Use(Acme);
        var south = await Slipway.Open("South", Token);
        var company = RefCompany.Named("acme");
        await company.Save(cancellationToken: Token);

        var failures = await Slipway.DryRunLetTo(south, 0, company.Id, Token);

        Assert.Empty(failures);
    }

    [Fact]
    public async Task A_reference_to_another_tenants_row_is_told_in_its_sentence_under_its_field()
    {
        await using var database = await Start();
        Guid theirs;
        using (Tenant.Use(Globex))
        {
            var company = RefCompany.Named("globex");
            await company.Save(cancellationToken: Token);
            theirs = company.Id;
        }

        using var acme = Tenant.Use(Acme);
        var south = await Slipway.Open("South", Token);

        var failures = await Slipway.DryRunLetTo(south, 0, theirs, Token);

        var failure = Assert.Single(failures);
        Assert.Equal("No such company.", failure.Message);
        Assert.Equal(["RefCompanyId"], failure.Fields);
    }

    [Fact]
    public async Task A_delete_a_row_still_points_at_is_told_in_the_entitys_sentence_and_nothing_is_deleted()
    {
        await using var database = await Start();
        using var acme = Tenant.Use(Acme);
        var slipway = await Slipway.Open("North", Token);
        await Hawser.At(slipway).Save(cancellationToken: Token);

        var failures = await Slipway.DryRunDelete(slipway, 0, Token);

        Assert.Equal(Slipway.InUse, Assert.Single(failures).Message);
        Assert.Equal(1, await Slipway.Count(cancellationToken: Token));
    }

    [Fact]
    public async Task A_delete_nothing_points_at_is_told_nothing()
    {
        await using var database = await Start();
        using var acme = Tenant.Use(Acme);
        var slipway = await Slipway.Open("North", Token);

        var failures = await Slipway.DryRunDelete(slipway, 0, Token);

        Assert.Empty(failures);
    }

    private Task<TestDatabase> Start() => TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}
