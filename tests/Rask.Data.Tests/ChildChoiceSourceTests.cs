using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
///     A child entity's read face as a picker's rows — <c>Cell.Choice(id, …)</c>, <c>Cell.Choices(…)</c>,
///     <c>Cell.ChoicePage(…)</c> — read through its parent's tenant, as an aggregate's are through its own.
/// </summary>
public sealed class ChildChoiceSourceTests : IDisposable
{
    private static readonly Guid Acme = new("8c3e1d4f-3c8e-4f0a-9d2c-0000000000a1");
    private static readonly Guid Globex = new("8c3e1d4f-3c8e-4f0a-9d2c-0000000000b2");

    private readonly InMemorySqlite _sqlite = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task A_childs_page_holds_only_the_rows_under_the_tenants_parents()
    {
        await using var database = await Start();
        await Seed();

        using (Tenant.Use(Acme))
        {
            var page = await Cell.ChoicePage(c => c.Id, "", 0, 10, cancellationToken: Token);

            Assert.Equal(["a1", "a2"], page.Rows.Select(r => r.Text));
            Assert.Equal(2, page.Total);
        }
    }

    [Fact]
    public async Task A_childs_choice_names_its_own_row_and_nothing_under_another_tenants_parent()
    {
        await using var database = await Start();
        await Seed();

        using (Tenant.Use(Acme))
        {
            var mine = await Cell.Choice("a1", c => c.Id, c => c.OutfitId, Token);
            var theirs = await Cell.Choice("g1", c => c.Id, cancellationToken: Token);

            Assert.Equal(new ChoiceRow<string>("a1", "a1", "oa"), mine);
            Assert.Null(theirs);
        }
    }

    [Fact]
    public async Task A_childs_source_finds_what_was_typed_and_names_the_picked_rows()
    {
        await using var database = await Start();
        await Seed();

        using (Tenant.Use(Acme))
        {
            var page = await Cell.Choices(c => c.Id).Page("2", 0, 10, picked: ["a1", "g1"], cancellationToken: Token);

            Assert.Equal(["a2"], page.Rows.Select(r => r.Id));
            Assert.Equal(["a1"], page.Picked.Select(r => r.Id));
        }
    }

    // Acme's cistern holds cells a1 and a2, Globex's holds g1.
    private static async Task Seed()
    {
        using (Tenant.Use(Acme))
        {
            await Cistern.Built("ca", ("a1", "oa"), ("a2", "oa")).Save(cancellationToken: Token);
        }

        using (Tenant.Use(Globex))
        {
            await Cistern.Built("cg", ("g1", "ob")).Save(cancellationToken: Token);
        }
    }

    private Task<TestDatabase> Start() => TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}
