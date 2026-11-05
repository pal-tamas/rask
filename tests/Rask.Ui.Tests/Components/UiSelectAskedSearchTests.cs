using Rask.Core;
using Rask.Core.Diagnostics;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     A select whose search the page answers through <c>Search</c> (#1284): what both apps wrote as a picker of their
///     own — TA's in memory, best 20; Eszaf's from the database — held by the select.
/// </summary>
/// <remarks>
///     The pause is counted in the browser (rask-input.ts); here the browser's part is played by the test, which sends
///     what the field says once typing has paused. Flux's backend search (fluxui.dev/components/select, "Backend search")
///     is the page's <c>$search</c> and a query over it, behind <c>:filter="false"</c>.
/// </remarks>
[Collection(AskedSearchCollection.Name)]
public partial class UiSelectAskedSearchTests : global::Rask.Core.RaskMarkup
{
    private const string Field = "[data-ui-select-search] input";

    private static readonly (int Id, string Name)[] Partners =
    [
        (1, "Glock"), (2, "Beretta"), (3, "Walther"), (4, "Bersa"), (5, "Sig Sauer"), (6, "Taurus"), (7, "Colt"),
    ];

    private readonly List<string> _asked = [];
    private int? _picked;
    private ICollection<int> _several = [];

    // What a store answers: the first three that hold what was typed. Answered at once, so the test reads the render
    // that follows without waiting; one test below waits on a store that is not.
    private async Task<IEnumerable<Component?>> Find(string typed)
    {
        _asked.Add(typed);

        return Matching(typed);
    }

    private static IEnumerable<Component?> Matching(string typed) =>
        Partners.Where(p => p.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).Take(3)
            .Select(p => Ui.SelectOption.Key(p.Id).Value(p.Id)[p.Name]);

    private Component One() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Search(Find).Placeholder("Choose…");

    private Component Several() =>
        Ui.Select.Values(_several).OnChange(values => _several = values).Listbox.Multiple().Search(Find).Placeholder("Choose…");

    private static string[] Rows(Page page) => [.. page.FindAll("[role=\"option\"]").Select(row => row.TextContent.Trim())];

    private static Task Pause(Page page, string text, string field = Field) =>
        page.On(field).Raise("pause", "{\"value\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}");

    // The row is pressed: a select the page answers holds no pick for the next action, it hears it at once.
    private static Task Pick(Page page, string words) =>
        page.On("#" + page.FindAll("[role=\"option\"]").Single(row => row.TextContent.Trim() == words).Attribute("id")).Click();

    [Fact]
    public void Before_anything_is_typed_the_list_offers_what_an_empty_search_finds()
    {
        var page = Acting.Render(One);

        var rows = Rows(page);

        Assert.Equal([""], _asked);
        Assert.Equal(["Glock", "Beretta", "Walther"], rows);
        Assert.NotNull(page.Find("[data-ui-select-button]"));
        Assert.NotNull(page.Find(Field));
    }

    [Fact]
    public void A_store_that_answers_later_fills_the_list_when_it_does()
    {
        var page = Acting.Render(() =>
            Ui.Select.Value(_picked).OnChange(value => _picked = value).Search(async typed =>
            {
                await Task.Delay(20.Milliseconds);
                return Matching(typed);
            }));

        page.Shows(html => html.Contains("Beretta", StringComparison.Ordinal));

        Assert.Equal(["Glock", "Beretta", "Walther"], Rows(page));
    }

    [Fact]
    public async Task What_is_typed_is_asked_once_typing_pauses_and_the_answer_replaces_the_rows()
    {
        var page = Acting.Render(One);

        await Pause(page, " ber ");

        Assert.Equal("250", page.Find(Field).Attribute("data-rask-debounce"));
        Assert.NotNull(page.Find(Field).Attribute("data-rask-on-pause"));
        Assert.Equal(["", "ber"], _asked);
        Assert.Equal(["Beretta", "Bersa"], Rows(page));
    }

    [Fact]
    public void The_search_slots_debounce_is_the_pause_the_select_waits()
    {
        var page = Acting.Render(() =>
            Ui.Select.Value(_picked).OnChange(value => _picked = value).Search(Find)[
                Ui.SelectSearch.Placeholder("Search partners...").Debounce(400.Milliseconds)
            ]);

        var field = page.Find(Field);

        Assert.Equal("400", field.Attribute("data-rask-debounce"));
        Assert.Equal("Search partners...", field.Attribute("placeholder"));
    }

    [Fact]
    public async Task A_picked_option_stays_first_in_the_list_while_a_search_does_not_find_it()
    {
        var page = Acting.Render(One);
        await Pick(page, "Walther");

        await Pause(page, "ber");

        Assert.Equal(3, _picked);
        Assert.Equal(["Walther", "Beretta", "Bersa"], Rows(page));
        Assert.Equal("Walther", page.TextOf("[data-ui-select-button]"));
        Assert.NotNull(page.Find("[data-ui-option][data-selected]"));
    }

    [Fact]
    public async Task A_kept_option_leaves_the_list_once_the_value_no_longer_holds_it()
    {
        var page = Acting.Render(One);
        await Pick(page, "Walther");
        await Pause(page, "ber");

        _picked = null;
        await Pause(page, "bers");

        Assert.Equal(["Bersa"], Rows(page));
    }

    [Fact]
    public void The_option_written_as_a_child_names_the_value_before_any_search_finds_it()
    {
        _picked = 7;

        var page = Acting.Render(() =>
            Ui.Select.Value(_picked).OnChange(value => _picked = value).Search(Find).Placeholder("Choose…")[
                Ui.SelectOption.Value(7)["Colt"]
            ]);

        Assert.Equal("Colt", page.TextOf("[data-ui-select-button]"));
        Assert.Equal(["Colt", "Glock", "Beretta", "Walther"], Rows(page));
    }

    [Fact]
    public async Task A_search_that_finds_nothing_says_so_and_keeps_the_picked_option()
    {
        var page = Acting.Render(One);
        await Pick(page, "Glock");

        await Pause(page, "zzz");

        Assert.Equal(["Glock"], Rows(page));
        Assert.Null(page.Find("[data-ui-listbox-empty]").Attribute("data-hidden"));
        Assert.Equal("No results found", page.TextOf("[data-ui-listbox-empty]"));
    }

    [Fact]
    public void The_list_has_a_loading_row_the_waiting_field_shows_and_the_no_results_row_steps_aside_for()
    {
        var page = Acting.Render(One);

        var loading = page.Find("[data-ui-listbox-loading]");

        Assert.Equal("Loading...", page.TextOf("[data-ui-listbox-loading]"));
        Assert.Contains("hidden", loading.Attribute("class")!.Split(' '));
        Assert.Contains("in-[[data-rask-list]:has(input[data-loading])]:block", loading.Attribute("class")!.Split(' '));
        Assert.Contains("in-[[data-rask-list]:has(input[data-loading])]:hidden", page.Find("[data-ui-listbox-empty]").Attribute("class")!.Split(' '));
    }

    [Fact]
    public void The_empty_slot_words_the_loading_row_and_the_no_results_row()
    {
        var page = Acting.Render(() =>
            Ui.Select.Value(_picked).OnChange(value => _picked = value).Search(Find)[
                Ui.SelectOptionEmpty.WhenLoading("Looking for partners...")["No partner found."]
            ]);

        var loading = page.TextOf("[data-ui-listbox-loading]");
        var empty = page.TextOf("[data-ui-listbox-empty]");

        Assert.Equal("Looking for partners...", loading);
        Assert.Equal("No partner found.", empty);
    }

    [Fact]
    public void A_hungarian_visitor_reads_the_loading_and_no_results_rows_in_hungarian()
    {
        var html = KitCulture.In("hu-HU", () => Page.Render(One).Html);

        var page = Page.Render(() => Raw.Value(html));

        Assert.Equal("Betöltés…", page.TextOf("[data-ui-listbox-loading]"));
        Assert.Equal("Nincs találat", page.TextOf("[data-ui-listbox-empty]"));
    }

    [Fact]
    public async Task An_answer_held_in_memory_is_a_plain_filter()
    {
        var page = Acting.Render(() =>
            Ui.Select.Value(_picked).OnChange(value => _picked = value).Search(async typed => Matching(typed)));

        await Pause(page, "sa");

        Assert.Equal(["Bersa", "Sig Sauer"], Rows(page));
    }

    [Fact]
    public async Task A_combobox_asks_from_its_own_input()
    {
        var page = Acting.Render(() =>
            Ui.Select.Value(_picked).OnChange(value => _picked = value).Combobox.Search(Find));
        const string input = "input[role=\"combobox\"]";

        await page.On(input).Raise("pause", "{\"value\":\"wal\"}");

        Assert.False(page.Exists(Field));
        Assert.Equal("250", page.Find(input).Attribute("data-rask-debounce"));
        Assert.Equal(["Walther"], Rows(page));
    }

    [Fact]
    public async Task Several_picked_options_stay_in_the_list_and_are_counted_whatever_a_search_finds()
    {
        var page = Acting.Render(Several);
        await Pick(page, "Glock");
        await Pick(page, "Walther");

        await Pause(page, "sa");

        Assert.Equal([1, 3], _several);
        Assert.Equal(["Glock", "Walther", "Bersa", "Sig Sauer"], Rows(page));
        Assert.Equal("2 selected", page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public async Task A_pillbox_asks_the_page_and_keeps_a_pill_for_each_option_a_search_left_out()
    {
        var page = Acting.Render(() =>
            Ui.Pillbox.Values(_several).OnChange(values => _several = values).Search(async typed =>
                Partners.Where(p => p.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).Take(3)
                    .Select(p => Ui.PillboxOption.Key(p.Id).Value(p.Id)[p.Name])));
        await Pick(page, "Glock");
        await Pick(page, "Beretta");

        await Pause(page, "colt", "[data-ui-pillbox-search] input");

        Assert.Equal([1, 2], _several);
        Assert.Equal(["Glock", "Beretta", "Colt"], Rows(page));
        Assert.Equal(["Glock", "Beretta"], page.FindAll("[data-ui-pillbox] div[data-value]").Select(pill => pill.TextContent.Trim()));
    }

    // Both apps keyed their options by the bound member and the id, because two pickers over the same ids on one page
    // were one set of keys to the page: it reported the repeat, and an option's instance moved between them.
    [Fact]
    public async Task Two_selects_on_one_page_may_key_their_options_alike()
    {
        var reports = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.ResetReportOnceForTests();
        RaskDiagnostics.Sink = reports.Add;
        string[] rows;
        try
        {
            int? company = null;
            int? carrier = null;
            var shown = 3;
            var page = Acting.Render(() => Fragment[
                Ui.Select.Value(company).OnChange(value => { company = value; shown = 1; }).Listbox[
                    Partners.Take(shown).Select(p => Ui.SelectOption.Key(p.Id).Value(p.Id)[p.Name])],
                Ui.Select.Value(carrier).OnChange(value => carrier = value).Listbox[
                    Partners.Take(3).Select(p => Ui.SelectOption.Key(p.Id).Value(p.Id)["Carrier " + p.Name])]
            ]);

            await Pick(page, "Glock");
            rows = Rows(page);
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
            RaskDiagnostics.ResetReportOnceForTests();
        }

        Assert.DoesNotContain(reports, report => report.Message.Contains(nameof(UiSelectOption), StringComparison.Ordinal));
        Assert.Equal(["Glock", "Carrier Glock", "Carrier Beretta", "Carrier Walther"], rows);
    }
}

/// <summary>The diagnostics sink is one for the process: what these tests read from it is theirs alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AskedSearchCollection
{
    public const string Name = "Asked search";
}
