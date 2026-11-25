using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
///     A read face as a picker's rows: <c>X.Choice(id, …)</c>, <c>X.Choices(…)</c> and <c>X.ChoicePage(…)</c> — searched,
///     ordered and paged in the database, never naming another tenant's row.
/// </summary>
public sealed class ChoiceSourceTests : IDisposable
{
    private static readonly Guid Acme = new("7b2f0c3e-2b7d-4e9a-8c1b-0000000000a1");
    private static readonly Guid Globex = new("7b2f0c3e-2b7d-4e9a-8c1b-0000000000b2");

    private readonly InMemorySqlite _sqlite = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task Choice_names_the_row_its_id_names()
    {
        await using var database = await Start();
        var id = await Company(Acme, "Acme Kft");

        using (Tenant.Use(Acme))
        {
            var choice = await RefCompany.Choice(id, c => c.Name, cancellationToken: Token);

            Assert.Equal(new ChoiceRow<Guid>(id, "Acme Kft"), choice);
        }
    }

    [Fact]
    public async Task Choice_is_null_for_another_tenants_row_for_none_and_for_no_id()
    {
        await using var database = await Start();
        var globex = await Company(Globex, "Globex Zrt");

        using (Tenant.Use(Acme))
        {
            var theirs = await RefCompany.Choice(globex, c => c.Name, cancellationToken: Token);
            var none = await RefCompany.Choice(Guid.NewGuid(), c => c.Name, cancellationToken: Token);
            var nothing = await RefCompany.Choice(null, c => c.Name, cancellationToken: Token);

            Assert.Equal((null, null, null), (theirs, none, nothing));
        }
    }

    [Fact]
    public async Task A_page_holds_what_was_typed_case_aside_in_order_with_the_whole_count()
    {
        await using var database = await Start();
        foreach (var name in new[] { "Kovacs es tarsa", "Bodnar", "KOVACS Bt", "Szabo", "kovacs" })
        {
            await Company(Acme, name);
        }

        using (Tenant.Use(Acme))
        {
            var page = await RefCompany.ChoicePage(c => c.Name, " kovacs ", 0, 2, cancellationToken: Token);

            Assert.Equal(["KOVACS Bt", "Kovacs es tarsa"], page.Rows.Select(r => r.Text));
            Assert.Equal(3, page.Total);
            Assert.Empty(page.Picked);
        }
    }

    [Fact]
    public async Task Pages_follow_on_without_a_row_on_two_of_them()
    {
        await using var database = await Start();
        for (var i = 0; i < 5; i++)
        {
            await Company(Acme, "same");
        }

        using (Tenant.Use(Acme))
        {
            var source = RefCompany.Choices(c => c.Name);
            var first = await source.Page("", 0, 3, cancellationToken: Token);
            var second = await source.Page("", 3, 3, cancellationToken: Token);

            Assert.Equal(5, first.Rows.Concat(second.Rows).Select(r => r.Id).Distinct().Count());
            Assert.Equal((5, 5), (first.Total, second.Total));
        }
    }

    [Fact]
    public async Task The_picked_rows_are_named_off_the_page_and_another_tenants_never()
    {
        await using var database = await Start();
        var held = await Company(Acme, "Zebra");
        await Company(Acme, "Alfa");
        var globex = await Company(Globex, "Globex Zrt");

        using (Tenant.Use(Acme))
        {
            var page = await RefCompany.ChoicePage(c => c.Name, "", 0, 1, picked: [held, globex], cancellationToken: Token);

            Assert.Equal(["Alfa"], page.Rows.Select(r => r.Text));
            Assert.Equal([new ChoiceRow<Guid>(held, "Zebra")], page.Picked);
            Assert.Equal(2, page.Total);
        }
    }

    [Fact]
    public async Task A_detail_is_shown_under_the_words_and_found_by_too()
    {
        await using var database = await Start();
        await Company(Acme, "Acme Kft");

        using (Tenant.Use(Acme))
        {
            var page = await RefCompany.Choices(c => c.Name, c => "dept " + c.Name).Page("DEPT", 0, 10, cancellationToken: Token);

            Assert.Equal(["dept Acme Kft"], page.Rows.Select(r => r.Detail));
        }
    }

    [Fact]
    public async Task A_filtered_query_offers_only_its_own_rows()
    {
        await using var database = await Start();
        await Company(Acme, "Alfa");
        await Company(Acme, "Béta");

        using (Tenant.Use(Acme))
        {
            var page = await RefCompany.Where(c => c.Name != "Alfa").Choices(c => c.Id, c => c.Name).Page("", 0, 10, cancellationToken: Token);

            Assert.Equal(["Béta"], page.Rows.Select(r => r.Text));
        }
    }

    private static async Task<Guid> Company(Guid tenant, string name)
    {
        using (Tenant.Use(tenant))
        {
            var company = RefCompany.Named(name);
            await company.Save(cancellationToken: Token);
            return company.Id;
        }
    }

    private Task<TestDatabase> Start() => TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}
