using System.Globalization;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>.Choices(Company.Choices(c =&gt; c.Name))</c>: a read face's rows, searched and paged by the source, the picked row
///     named on no page the reader reached — what TA's ChoicePages and Eszaf's PagedChoices wrote around a paging select.
/// </summary>
[Collection(AskedSearchCollection.Name)]
public partial class UiSourceChoicesTests : global::Rask.Core.RaskMarkup
{
    private const string Box = "[data-rask-list-window]";

    private const string Field = "[data-ui-select-search] input";

    private sealed class Order
    {
        public int? CompanyId { get; set; }

        public int SupplierId { get; set; }

        public ICollection<int> CompanyIds { get; set; } = [];
    }

    private static string NameOf(int id) => "Company " + id.ToString("000", CultureInfo.InvariantCulture);

    private static string[] Rows(Page page) =>
        [.. page.FindAll("[role=\"option\"]").Select(row => row.Attribute("data-rask-list-text") ?? row.TextContent.Trim())];

    private static Task Scroll(Page page, int row) =>
        page.On(Box).Raise(
            "scroll",
            "{\"target\":{\"scrollTop\":" + (row * 32).ToString(CultureInfo.InvariantCulture) + ",\"clientHeight\":320,\"scrollHeight\":64000}}");

    private static Task Pause(Page page, string text) =>
        page.On(Field).Raise("pause", "{\"value\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}");

    [Fact]
    public void A_picked_row_on_no_page_reached_names_the_select_and_stands_first()
    {
        var companies = new Companies(500);
        var order = new Order { CompanyId = 450 };

        var page = Acting.Render(() => Ui.Select.Bind(() => order.CompanyId).Live().Choices(companies.Source()));

        Assert.Equal(NameOf(450), page.TextOf("[data-ui-select-button]"));
        Assert.Equal([NameOf(450), NameOf(1)], Rows(page)[..2]);
        Assert.Equal([("", 0, 50, "450")], companies.Asked);
    }

    [Fact]
    public async Task The_next_page_skips_the_rows_the_list_holds_and_not_the_picked_one_put_before_them()
    {
        var companies = new Companies(500);
        var order = new Order { CompanyId = 450 };
        var page = Acting.Render(() => Ui.Select.Bind(() => order.CompanyId).Live().Choices(companies.Source()));

        await Scroll(page, 30);
        await Scroll(page, 60);

        Assert.Equal(("", 50, ""), (companies.Asked[^1].Typed, companies.Asked[^1].Skip, companies.Asked[^1].Picked));
        Assert.Contains(NameOf(51), Rows(page));
        Assert.Equal(Rows(page).Length, Rows(page).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task What_is_typed_is_searched_by_the_source_and_the_picked_row_stays()
    {
        var companies = new Companies(500);
        var order = new Order { CompanyId = 450 };
        var page = Acting.Render(() => Ui.Select.Bind(() => order.CompanyId).Live().Choices(companies.Source()));

        await Pause(page, "12");

        Assert.Equal(("12", 0, 50, "450"), companies.Asked[^1]);
        Assert.Equal(NameOf(450), Rows(page)[0]);
        Assert.Equal(NameOf(12), Rows(page)[1]);
    }

    [Fact]
    public void A_source_built_again_each_render_is_not_asked_again_for_nothing()
    {
        var companies = new Companies(500);
        var order = new Order();
        var page = Acting.Render(() => Ui.Select.Bind(() => order.CompanyId).Live().Choices(companies.Source()));

        page.Render();

        Assert.Single(companies.Asked);
    }

    [Fact]
    public void A_select_of_a_plain_id_takes_the_source_as_a_nullable_one_does()
    {
        var companies = new Companies(20);
        var order = new Order { SupplierId = 7 };

        var page = Acting.Render(() => Ui.Select.Bind(() => order.SupplierId).Choices(companies.Source()));

        Assert.Equal(NameOf(7), page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public void A_pillbox_names_every_picked_row_whatever_page_it_is_on()
    {
        var companies = new Companies(500);
        var order = new Order { CompanyIds = [300, 450] };

        var page = Acting.Render(() => Ui.Pillbox.Bind(() => order.CompanyIds).Live().Choices(companies.Source()));

        var trigger = page.Find("[data-ui-pillbox-trigger]").TextContent;
        Assert.Contains(NameOf(300), trigger, StringComparison.Ordinal);
        Assert.Contains(NameOf(450), trigger, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_field_over_an_id_offers_the_source()
    {
        var companies = new Companies(20);
        var order = new Order { CompanyId = 3 };

        var page = Acting.Render(() => Ui.Field.Bind(() => order.CompanyId).Choices(companies.Source()));

        Assert.Equal(NameOf(3), page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public void A_picked_id_the_source_has_no_row_for_names_nothing()
    {
        var companies = new Companies(20);
        var order = new Order { CompanyId = 999 };

        var page = Acting.Render(() => Ui.Select.Bind(() => order.CompanyId).Choices(companies.Source()));

        Assert.DoesNotContain("999", page.TextOf("[data-ui-select-button]"), StringComparison.Ordinal);
        Assert.Equal(NameOf(1), Rows(page)[0]);
    }

    // A read face's source in memory: the rows whose words hold what was typed, in the order of their ids, each answer
    // recorded — what was typed, the page, and the picked ids it was asked to name.
    private sealed class Companies(int count)
    {
        public List<(string Typed, int Skip, int Take, string Picked)> Asked { get; } = [];

        // A new source every render, as a page's `Company.Choices(c => c.Name)` is.
        public IChoiceSource<int> Source() => new Source(this);

        public ChoiceRow<int>[] Matching(string typed) =>
        [
            .. Enumerable.Range(1, count).Select(id => new ChoiceRow<int>(id, NameOf(id)))
                .Where(row => row.Text.Contains(typed, StringComparison.OrdinalIgnoreCase)),
        ];

        public ChoiceRow<int>? Row(int id) => id >= 1 && id <= count ? new ChoiceRow<int>(id, NameOf(id)) : null;
    }

    private sealed class Source(Companies companies) : IChoiceSource<int>
    {
        public Task<ChoiceRowPage<int>> Page(
            string typed, int skip, int take, IReadOnlyCollection<int>? picked = null, CancellationToken cancellationToken = default)
        {
            companies.Asked.Add((typed, skip, take, string.Join(',', picked ?? [])));
            var found = companies.Matching(typed);
            ChoiceRow<int>[] held = [.. (picked ?? []).Select(companies.Row).OfType<ChoiceRow<int>>()];
            return Task.FromResult(new ChoiceRowPage<int>([.. found.Skip(skip).Take(take)], found.Length, held));
        }

        public Task<ChoiceRow<int>?> Find(int id, CancellationToken cancellationToken = default) => Task.FromResult(companies.Row(id));
    }
}
