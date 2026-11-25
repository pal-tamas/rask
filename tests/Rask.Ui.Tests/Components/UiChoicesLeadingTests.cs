using System.Globalization;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>.Leading(…)</c> after <c>.Choices(…)</c> on <c>Ui.Select</c> and <c>Ui.Pillbox</c> (#1361): hand rows pinned
///     before the sorted, searched or paged choices — Eszaf's "Nincs cég" first in its company filter.
/// </summary>
[Collection(AskedSearchCollection.Name)]
public partial class UiChoicesLeadingTests : global::Rask.Core.RaskMarkup
{
    private const string Field = "[data-ui-select-search] input";

    private static readonly Ui.Choice<int> NoCompany = new(0, "Nincs cég");

    private static readonly Ui.Choice<int> AnyCompany = new(-1, "Bármelyik");

    private static readonly Ui.Choice<int>[] Companies =
    [
        new(3, "Walther"), new(1, "Bergmann"), new(2, "Ábel Kft."), new(4, "Alma Zrt."),
    ];

    private readonly List<(string Typed, int Skip, int Take)> _asked = [];

    private sealed class Filter
    {
        public int? CompanyId { get; set; }

        public int Floor { get; set; } = 1;

        public List<int> CompanyIds { get; set; } = [];
    }

    private static string[] Rows(Page page) =>
        [.. page.FindAll("[role=\"option\"]").Select(row => row.Attribute("data-rask-list-text") ?? row.TextContent.Trim())];

    private static Task Pause(Page page, string text, string field = Field) =>
        page.On(field).Raise("pause", "{\"value\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}");

    // The box scrolled so `row` is its top row.
    private static Task Scroll(Page page, int row) =>
        page.On("[data-rask-list-window]").Raise(
            "scroll",
            "{\"target\":{\"scrollTop\":" + (row * 32).ToString(CultureInfo.InvariantCulture) + ",\"clientHeight\":320,\"scrollHeight\":64000}}");

    private static string NameOf(int id) => "Product " + id.ToString("000", CultureInfo.InvariantCulture);

    // A store of `count` products in the order of their ids: the page of those whose name holds what was typed.
    private Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<int>>>> Store(int count) => (typed, page) =>
    {
        _asked.Add((typed, page.Skip, page.Take));
        return Task.FromResult<IReadOnlyList<Ui.Choice<int>>>(
            [.. Enumerable.Range(1, count).Select(id => new Ui.Choice<int>(id, NameOf(id)))
                .Where(row => row.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).Skip(page.Skip).Take(page.Take)]);
    };

    [Fact]
    public void The_leading_rows_stand_before_the_sorted_choices_in_the_order_given()
    {
        var filter = new Filter();

        var page = Page.Render(() => Ui.Select.Bind(() => filter.CompanyId).Choices(Companies).Leading(NoCompany, AnyCompany));

        Assert.Equal(["Nincs cég", "Bármelyik", "Ábel Kft.", "Alma Zrt.", "Bergmann", "Walther"], Rows(page));
    }

    [Fact]
    public void A_leading_row_is_kept_by_every_search_and_the_choices_are_not()
    {
        var filter = new Filter();

        var page = Page.Render(() => Ui.Select.Bind(() => filter.CompanyId).Choices(Companies).Leading(NoCompany));

        var kept = page.FindAll("[role=\"option\"]").Select(row => row.Attribute("data-rask-list-keep") is not null).ToArray();
        Assert.Equal([true, false, false, false, false], kept);
    }

    [Fact]
    public void A_select_of_a_value_that_cannot_be_unset_takes_leading_rows_of_its_own_type()
    {
        var filter = new Filter();

        var page = Page.Render(() => Ui.Select.Bind(() => filter.Floor).Choices(Companies).Leading(NoCompany));

        Assert.Equal("Nincs cég", Rows(page)[0]);
    }

    [Fact]
    public async Task A_leading_row_is_picked_and_written_as_any_row_is()
    {
        var filter = new Filter { CompanyId = 3 };
        var page = Acting.Render(() => Ui.Select.Bind(() => filter.CompanyId).Live().Choices(
            typed => Task.FromResult<IReadOnlyList<Ui.Choice<int>>>(Companies), held: Companies[0]).Leading(NoCompany));

        await page.On("#" + page.FindAll("[role=\"option\"]").Single(row => row.TextContent.Trim() == "Nincs cég").Attribute("id")).Click();

        Assert.Equal(0, filter.CompanyId);
    }

    [Fact]
    public async Task A_windowed_list_narrowed_by_a_search_keeps_its_leading_row_first()
    {
        var filter = new Filter();
        Ui.Choice<int>[] many = [.. Enumerable.Range(1, 500).Select(id => new Ui.Choice<int>(id, NameOf(id)))];
        var page = Acting.Render(() => Ui.Select.Bind(() => filter.CompanyId).Choices(many).Leading(NoCompany));

        await Pause(page, "product 49");

        Assert.Equal(["Nincs cég", "Product 490", "Product 491", "Product 492"], Rows(page)[..4]);
    }

    [Fact]
    public async Task A_searched_source_answers_with_the_leading_rows_first_whatever_was_typed()
    {
        var filter = new Filter();
        var asked = new List<string>();
        var page = Acting.Render(() => Ui.Select.Bind(() => filter.CompanyId).Live().Choices(typed =>
        {
            asked.Add(typed);
            return Task.FromResult<IReadOnlyList<Ui.Choice<int>>>([.. Companies.Where(c => c.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))]);
        }).Leading(NoCompany));

        await Pause(page, "berg");

        Assert.Equal(["", "berg"], asked);
        Assert.Equal(["Nincs cég", "Bergmann"], Rows(page));
    }

    [Fact]
    public async Task A_paged_source_asks_its_first_page_for_as_many_fewer_rows_and_never_repeats_the_leading_ones()
    {
        var filter = new Filter();
        var store = Store(500);
        var page = Acting.Render(() => Ui.Select.Bind(() => filter.CompanyId).Live().Choices(store).Leading(NoCompany));
        var top = Rows(page)[..3];

        await Scroll(page, 45);

        Assert.Equal(["Nincs cég", "Product 001", "Product 002"], top);
        Assert.Equal([("", 0, 49), ("", 49, 50)], _asked);
        Assert.Equal("32 1 100", page.Find("[data-rask-list-window]").Attribute(UiWindowMarks.Window));
        Assert.Contains(NameOf(50), Rows(page));
    }

    [Fact]
    public async Task Typing_into_a_paged_source_keeps_the_leading_row_above_its_first_page()
    {
        var filter = new Filter();
        var store = Store(500);
        var page = Acting.Render(() => Ui.Select.Bind(() => filter.CompanyId).Live().Choices(store).Leading(NoCompany));

        await Pause(page, "product 4");

        Assert.Equal(("product 4", 0, 49), _asked[^1]);
        Assert.Equal(["Nincs cég", "Product 400", "Product 401"], Rows(page)[..3]);
    }

    // A store of `count` products that says how many of them hold what was typed (Ui.ChoiceRows).
    private Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<int>>>> Counted(int count) => (typed, page) =>
    {
        _asked.Add((typed, page.Skip, page.Take));
        var matching = Enumerable.Range(1, count).Select(id => new Ui.Choice<int>(id, NameOf(id)))
            .Where(row => row.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult<IReadOnlyList<Ui.Choice<int>>>(
            new Ui.ChoiceRows<int>([.. matching.Skip(page.Skip).Take(page.Take)], matching.Count));
    };

    private static readonly Ui.Choice<int> NoProduct = new(0, "No product");

    private static string[] SetSizes(Page page) =>
        [.. page.FindAll("[role=\"option\"]").Select(row => row.Attribute("aria-setsize") ?? "").Distinct()];

    [Fact]
    public void A_counted_source_is_as_long_as_its_total_and_the_leading_rows_from_its_first_page()
    {
        var filter = new Filter();
        var store = Counted(500);

        var page = Acting.Render(() => Ui.Select.Bind(() => filter.CompanyId).Live().Choices(store).Leading(NoProduct));

        Assert.Equal([("", 0, 49)], _asked);
        Assert.Equal(["501"], SetSizes(page));
        Assert.Equal("32 1 501", page.Find("[data-rask-list-window]").Attribute(UiWindowMarks.Window));
    }

    [Fact]
    public async Task A_counted_source_that_found_nothing_leaves_a_list_of_only_its_leading_rows()
    {
        var filter = new Filter();
        var store = Counted(500);
        var page = Acting.Render(() => Ui.Select.Bind(() => filter.CompanyId).Live().Choices(store).Leading(NoProduct));

        await Pause(page, "no such product");

        Assert.Equal(["No product"], Rows(page));
        Assert.Equal(["1"], SetSizes(page));
    }

    [Fact]
    public async Task A_later_page_of_a_counted_source_keeps_the_list_as_long_as_its_total_and_the_leading_rows()
    {
        var filter = new Filter();
        var store = Counted(500);
        var page = Acting.Render(() => Ui.Select.Bind(() => filter.CompanyId).Live().Choices(store).Leading(NoProduct));

        await Scroll(page, 45);

        Assert.Equal([("", 0, 49), ("", 49, 50)], _asked);
        Assert.Contains(NameOf(50), Rows(page));
        Assert.Equal(["501"], SetSizes(page));
        Assert.Equal("32 1 501", page.Find("[data-rask-list-window]").Attribute(UiWindowMarks.Window));
    }

    [Fact]
    public void A_source_that_does_not_say_its_total_reports_none_beside_its_leading_rows()
    {
        var filter = new Filter();
        var store = Store(500);

        var page = Acting.Render(() => Ui.Select.Bind(() => filter.CompanyId).Live().Choices(store).Leading(NoProduct));

        Assert.Equal(["-1"], SetSizes(page));
    }

    [Fact]
    public void A_pillbox_takes_leading_rows_before_its_sorted_choices()
    {
        var filter = new Filter();

        var page = Page.Render(() => Ui.Pillbox.Bind(() => filter.CompanyIds).Choices(Companies).Leading(NoCompany));

        Assert.Equal(["0", "2", "4", "1", "3"], page.FindAll("[role=\"option\"]").Select(row => row.Attribute("data-rask-key")));
        Assert.NotNull(page.Find("[role=\"option\"][data-rask-key=\"0\"]").Attribute("data-rask-list-keep"));
    }

    [Fact]
    public async Task A_paged_pillbox_puts_its_leading_row_above_the_first_page()
    {
        var filter = new Filter();
        var store = Store(500);
        var page = Acting.Render(() => Ui.Pillbox.Bind(() => filter.CompanyIds).Live().Choices(store).Leading(NoCompany));

        await Pause(page, "product 4", "[data-ui-pillbox-search] input");

        Assert.Equal(("product 4", 0, 49), _asked[^1]);
        Assert.Equal("0", page.FindAll("[role=\"option\"]")[0].Attribute("data-rask-key"));
    }

    [Fact]
    public async Task A_render_that_drops_the_leading_step_drops_its_rows()
    {
        var filter = new Filter();
        var leading = true;
        var page = Acting.Render(() => leading
            ? Ui.Select.Bind(() => filter.CompanyId).Choices(Companies).Leading(NoCompany)
            : Ui.Select.Bind(() => filter.CompanyId).Choices(Companies));
        leading = false;

        await Acting.Act(page);

        Assert.Equal(["Ábel Kft.", "Alma Zrt.", "Bergmann", "Walther"], Rows(page));
    }

    [Fact]
    public void Leading_rows_written_before_the_choices_name_the_order_to_write()
    {
        var filter = new Filter();

        var failure = Assert.ThrowsAny<Exception>(() => Page.Render(() => Ui.Select.Bind(() => filter.CompanyId).Leading(NoCompany).Choices(Companies)));

        Assert.Contains(".Choices(rows).Leading(none)", failure.ToString(), StringComparison.Ordinal);
    }
}
