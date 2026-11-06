using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>.Choices(…)</c> on <c>Ui.Select</c> and <c>Ui.Pillbox</c>: rows of another table, offered as the picker both
///     apps wrote of their own (TA's and Eszaf's <c>Picker</c>, Eszaf's <c>PickerMany</c> and <c>MultiPicker</c>) —
///     in the reader's alphabet, searched, cleared only where choosing nothing is an answer.
/// </summary>
[Collection(AskedSearchCollection.Name)]
public partial class UiChoicesTests : global::Rask.Core.RaskMarkup
{
    private const string ClearButton = "[aria-label=\"Clear selected\"]";

    private static readonly Ui.Choice<int>[] Companies =
    [
        new(3, "Walther"), new(1, "Bergmann", "Hamburg"), new(2, "Ábel Kft."), new(4, "Alma Zrt."),
    ];

    private sealed class Order
    {
        public int? CompanyId { get; set; } = 1;

        public int FloorId { get; set; } = 1;

        [Required]
        public int? DriverId { get; set; } = 1;

        public string? Code { get; set; } = "b";

        public List<int> VehicleIds { get; set; } = [1];
    }

    // Each row's words, without the line under them.
    private static string[] Rows(Page page) =>
        [.. page.FindAll("[role=\"option\"]").Select(row => row.Attribute("data-rask-list-text") ?? row.TextContent.Trim())];

    // The placeholder as the trigger says it; a held list draws it again in the faces the browser stamps.
    private static string Placeholder(Page page, string selector) => page.FindAll(selector)[0].TextContent.Trim();

    // The words the button shows once a row is picked; the line under them is in the button too, hidden there.
    private static string Shown(Page page) => page.FindAll("[data-ui-select-button] [data-rask-list-shows] [data-ui-text]") is [var line]
        ? page.TextOf("[data-ui-select-button] [data-rask-list-shows]").Replace(line.TextContent.Trim(), string.Empty, StringComparison.Ordinal).Trim()
        : page.TextOf("[data-ui-select-button] [data-rask-list-shows]");

    private static T InHungarian<T>(Func<T> render)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("hu-HU");
        try
        {
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Choices_draw_a_listbox_with_a_search_field_and_the_catalogs_placeholder()
    {
        var order = new Order { CompanyId = null };

        var page = Page.Render(() => Ui.Select.Bind(() => order.CompanyId).Choices(Companies));

        Assert.NotNull(page.Find("[data-ui-select-button]"));
        Assert.NotNull(page.Find("[data-ui-select-search] input"));
        Assert.Equal("Choose...", Placeholder(page, "[data-ui-select-placeholder]"));
        Assert.Equal(4, page.FindAll("[role=\"option\"]").Count);
    }

    [Fact]
    public void The_rows_are_in_the_readers_alphabet_whatever_order_they_came_in()
    {
        var order = new Order();

        var page = Page.Render(() => Ui.Select.Bind(() => order.CompanyId).Choices(Companies));

        Assert.Equal(["Ábel Kft.", "Alma Zrt.", "Bergmann", "Walther"], Rows(page));
    }

    [Fact]
    public void Hungarian_puts_an_accented_letter_with_its_own_and_a_double_letter_after_the_single_one()
    {
        string[] names = ["Zsolt", "Kár", "Kar", "Csaba", "Cukor", "Cecil", "Dzsenifer", "Dzéta", "Dorka", "Ödön", "Olga", "Ábel", "Alma", "Zoltán"];
        var order = new Order();

        var page = InHungarian(() => Page.Render(() =>
            Ui.Select.Bind(() => order.CompanyId).Choices(names.Select((name, at) => new Ui.Choice<int>(at, name)))));

        Assert.Equal(
            ["Ábel", "Alma", "Cecil", "Cukor", "Csaba", "Dorka", "Dzéta", "Dzsenifer", "Kar", "Kár", "Olga", "Ödön", "Zoltán", "Zsolt"],
            Rows(page));
    }

    [Fact]
    public void In_Hungarian_the_placeholder_is_the_catalogs()
    {
        var order = new Order { CompanyId = null };

        var placeholder = KitCulture.In("hu", () => Placeholder(Page.Render(() => Ui.Select.Bind(() => order.CompanyId).Choices(Companies)), "[data-ui-select-placeholder]"));

        Assert.Equal("Válasszon…", placeholder);
    }

    [Fact]
    public void A_rows_detail_is_the_line_under_its_words_and_a_search_finds_it_by_that_too()
    {
        var order = new Order();

        var page = Page.Render(() => Ui.Select.Bind(() => order.CompanyId).Choices(Companies));

        var bergmann = page.Find("[data-rask-list-value=\"1\"]");
        Assert.Contains("Hamburg", bergmann.TextContent, StringComparison.Ordinal);
        Assert.Equal("Hamburg", bergmann.Attribute("data-rask-list-keywords"));
        Assert.Equal("Bergmann", bergmann.Attribute("data-rask-list-text"));
        Assert.Equal("Bergmann", Shown(page));
    }

    [Fact]
    public void A_member_that_may_hold_nothing_is_cleared_with_the_cross()
    {
        var order = new Order();

        var page = Page.Render(() => Ui.Select.Bind(() => order.CompanyId).Choices(Companies));

        Assert.True(page.Exists(ClearButton));
    }

    [Fact]
    public void A_member_that_cannot_hold_nothing_offers_no_cross()
    {
        var order = new Order();

        var page = Page.Render(() => Ui.Select.Bind(() => order.FloorId).Choices(Companies));

        Assert.False(page.Exists(ClearButton));
        Assert.Equal("Bergmann", Shown(page));
    }

    [Fact]
    public void A_nullable_member_the_model_requires_offers_no_cross()
    {
        var order = new Order();

        var page = Page.Render(() => Ui.Select.Bind(() => order.DriverId).Choices(Companies));

        Assert.False(page.Exists(ClearButton));
        Assert.True(page.Exists("[aria-required=\"true\"]"));
    }

    [Fact]
    public void A_nullable_text_member_is_cleared_and_takes_rows_of_text()
    {
        var order = new Order();
        Ui.Choice<string>[] codes = [new("b", "Bravo"), new("a", "Alpha")];

        var page = Page.Render(() => Ui.Select.Bind(() => order.Code).Choices(codes));

        Assert.True(page.Exists(ClearButton));
        Assert.Equal(["Alpha", "Bravo"], Rows(page));
    }

    [Fact]
    public void What_the_page_says_wins_over_what_choices_fill_in()
    {
        var order = new Order { CompanyId = null };

        var page = Page.Render(() =>
            Ui.Select.Bind(() => order.CompanyId).Choices(Companies).Placeholder("Pick a company").Clearable(false).Searchable(false));

        Assert.False(page.Exists(ClearButton));
        Assert.False(page.Exists("[data-ui-select-search]"));
        Assert.Contains("Pick a company", page.Find("[data-ui-select-button]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_search_answers_with_rows_in_order_and_keeps_the_held_row_first_while_it_misses_it()
    {
        var order = new Order { CompanyId = 3 };
        var asked = new List<string>();

        var page = Page.Render(() => Ui.Select.Bind(() => order.CompanyId).Live().Choices(async typed =>
        {
            asked.Add(typed);
            await Task.Yield();
            return Companies.Where(c => c.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        }, held: Companies[0]));
        page.Shows(html => html.Contains("Bergmann", StringComparison.Ordinal));

        await page.On("[data-ui-select-search] input").Raise("pause", "{\"value\":\"m\"}");
        page.Shows(html => html.Contains("Alma", StringComparison.Ordinal));

        Assert.Equal(["", "m"], asked);
        Assert.Equal(["Walther", "Alma Zrt.", "Bergmann"], Rows(page));
        Assert.Equal("Walther", page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public void A_pillbox_of_rows_searches_them_in_the_readers_alphabet()
    {
        var order = new Order();

        var page = Page.Render(() => Ui.Pillbox.Bind(() => order.VehicleIds).Choices(Companies));

        Assert.Equal(["2", "4", "1", "3"], page.FindAll("[role=\"option\"]").Select(row => row.Attribute("data-rask-key")));
        Assert.Equal("Hamburg", page.Find("[role=\"option\"][data-rask-key=\"1\"] span.ms-2").TextContent);
        Assert.Equal("Bergmann", page.Find("[data-rask-list-value=\"1\"]").Attribute("data-rask-list-says"));
        Assert.NotNull(page.Find("[data-ui-pillbox-search] input"));
        Assert.Contains("Bergmann", page.Find("[data-ui-pillbox-trigger]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_pillbox_of_rows_says_the_catalogs_placeholder()
    {
        var order = new Order { VehicleIds = [] };

        var page = Page.Render(() => Ui.Pillbox.Bind(() => order.VehicleIds).Choices(Companies));

        Assert.Equal("Choose...", Placeholder(page, "[data-ui-pillbox-placeholder]"));
    }

    [Fact]
    public async Task A_searching_pillbox_keeps_its_held_pills_while_a_search_does_not_bring_them()
    {
        var order = new Order { VehicleIds = [3] };

        var page = Page.Render(() => Ui.Pillbox.Bind(() => order.VehicleIds).Choices(
            async typed =>
            {
                await Task.Yield();
                return Companies.Where(c => c.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToList();
            },
            held: [Companies[0]]));
        page.Shows(html => html.Contains("Bergmann", StringComparison.Ordinal));

        await page.On("[data-ui-pillbox-search] input").Raise("pause", "{\"value\":\"berg\"}");
        page.Shows(html => !html.Contains("Alma", StringComparison.Ordinal));

        Assert.Equal(["3", "1"], page.FindAll("[role=\"option\"]").Select(row => row.Attribute("data-rask-key")));
    }
}
