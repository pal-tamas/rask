namespace Rask.Ui.Tests.Components;

/// <summary>
///     The searchable select — Flux UI's combobox, as a MODE of the select rather than a second control.
/// </summary>
/// <remarks>
///     <para>
///         There is no <c>UiCombobox</c> to choose instead: a box you type into to narrow a fixed list of
///         answers is the same question a select asks, so it is the same component with a search box in
///         its popover. What turns it on is asking for something the platform's <c>&lt;select&gt;</c> has
///         nowhere to put — a search box, a custom idea of what matches, a clear button.
///     </para>
///     <para>
///         The search is C#, not script: the popover's input raises <c>input</c>, the component narrows
///         its own list and re-renders. Which is why these can be driven in process.
///     </para>
/// </remarks>
public partial class UiSelectSearchTests : global::Rask.Core.RaskMarkup
{
    private static readonly (string Value, string Text)[] Countries =
    [
        ("hu", "Hungary"), ("gb", "United Kingdom"), ("ie", "Ireland"), ("at", "Österreich")
    ];

    private static string Searchable() =>
        UiSelect.Value("hu").Options(Countries).Label("Country").Searchable(true).ToHtml();

    // The popover has to be OPEN before the search box is in the markup — it lives inside the popover,
    // above the list. The browser owns that state and reports it back through the toggle event.
    private static async Task<global::Rask.Testing.RenderedComponent> OpenedAsync(
        global::Rask.Core.Component select)
    {
        var page = global::Rask.Testing.Test.Render(select);
        await page.On("[popover]").RaiseAsync("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");
        return page;
    }

    [Fact]
    public void Asking_for_search_draws_the_list_here_rather_than_handing_it_to_the_platform()
    {
        // Native is the default and stays the default — but a <select> has nowhere to put a search box,
        // so asking for one chooses the drawn list rather than contradicting it.
        var html = Searchable();

        Assert.DoesNotContain("<select", html);
        Assert.Contains("role=\"combobox\"", html);
    }

    [Fact]
    public void A_filter_or_an_on_search_implies_it_without_being_asked_twice()
    {
        Assert.DoesNotContain("<select",
            UiSelect.Value("hu").Options(Countries).Label("Country")
                .Filter((v, text) => v.Contains(text, StringComparison.OrdinalIgnoreCase)).ToHtml());
        Assert.DoesNotContain("<select",
            UiSelect.Value("hu").Options(Countries).Label("Country").OnSearch(_ => { }).ToHtml());
    }

    [Fact]
    public void A_plain_drawn_list_has_no_search_box()
    {
        // Type-ahead is what a short list needs, and the drawn list has it without this.
        Assert.DoesNotContain("Search&#x2026;",
            UiSelect.Value("hu").Options(Countries).Label("Country").Native(false).ToHtml());
    }

    [Fact]
    public void The_box_is_inside_the_popover_and_only_once_it_is_open()
    {
        // Closed, there is nothing to search: the box would take focus and announce a list nobody opened.
        Assert.DoesNotContain("Search&#x2026;", Searchable());
    }

    [Fact]
    public async Task Opening_puts_a_named_search_box_over_the_list()
    {
        var page = await OpenedAsync(
            UiSelect.Value("hu").Options(Countries).Label("Country").Searchable(true));

        // Its own name, its own role, and it points at the list it narrows — aria-activedescendant only
        // announces an option from the element that actually holds focus, which is this one.
        Assert.Contains("aria-label=\"Search Country\"", page.Html, StringComparison.Ordinal);
        Assert.Contains("role=\"combobox\"", page.Html, StringComparison.Ordinal);
        Assert.Contains("aria-controls=", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typing_narrows_the_list_to_what_matches()
    {
        var page = await OpenedAsync(
            UiSelect.Value(default(string)).Options(Countries).Label("Country").Searchable(true));

        await page.On("#" + SearchId(page.Html)).InputAsync("ire");

        Assert.Contains("Ireland", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hungary", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_match_ignores_case_and_accents_in_the_visitors_own_culture()
    {
        // Somebody typing "oster" means to find "Österreich"; a reader who cannot type "Ö" on the
        // keyboard in front of them is not a reader to shut out.
        var page = await OpenedAsync(
            UiSelect.Value(default(string)).Options(Countries).Label("Country").Searchable(true));

        await page.On("#" + SearchId(page.Html)).InputAsync("oster");

        // Non-ASCII is encoded on the way out, so this is the text as it reaches the browser.
        Assert.Contains("&#xD6;sterreich", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hungary", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_filter_decides_what_matches_when_the_words_shown_are_not_the_whole_answer()
    {
        // Searching a country's CODE as well as its name: the control never assumes the shape of the
        // data, so the page says what a match is.
        var page = await OpenedAsync(
            UiSelect.Value(default(string)).Options(Countries).Label("Country")
                .Filter((v, text) => v.Contains(text, StringComparison.OrdinalIgnoreCase)));

        await page.On("#" + SearchId(page.Html)).InputAsync("gb");

        Assert.Contains("United Kingdom", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hungary", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_matching_says_so_rather_than_showing_an_empty_box()
    {
        var page = await OpenedAsync(
            UiSelect.Value("hu").Options(Countries).Label("Country").Searchable(true));

        await page.On("#" + SearchId(page.Html)).InputAsync("zzz");

        Assert.Contains("No results found", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_it_says_when_empty_is_the_pages_to_choose()
    {
        var page = await OpenedAsync(
            UiSelect.Value("hu").Options(Countries).Label("Country").Searchable(true)
                .EmptyText("No country by that name"));

        await page.On("#" + SearchId(page.Html)).InputAsync("zzz");

        Assert.Contains("No country by that name", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_search_hands_the_typing_over_and_filters_nothing_here()
    {
        // The page ran the query and handed back the answer, so narrowing it again would narrow it
        // twice — and would hide rows the server deliberately returned.
        var typed = new List<string>();
        var page = await OpenedAsync(
            UiSelect.Value(default(string)).Options(Countries).Label("Country").OnSearch(s => typed.Add(s)));

        await page.On("#" + SearchId(page.Html)).InputAsync("ire");

        Assert.Equal(["ire"], typed);
        Assert.Contains("Hungary", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void While_loading_the_list_says_so_instead_of_saying_nothing_matched()
    {
        // "No results found" over a list that has not arrived is a lie the reader acts on.
        var html = UiSelect.Value("hu").Options(Countries).Label("Country")
            .Searchable(true).Loading(true).ToHtml();

        Assert.Contains("Searching&#x2026;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("No results found", html, StringComparison.Ordinal);
    }

    [Fact]
    public void What_it_says_while_loading_is_the_pages_to_choose() =>
        Assert.Contains("Fetching countries&#x2026;",
            UiSelect.Value("hu").Options(Countries).Label("Country").Searchable(true)
                .Loading(true).LoadingText("Fetching countries…").ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void Clearing_is_offered_only_when_there_is_something_to_clear()
    {
        Assert.Contains("aria-label=\"Clear Country\"",
            UiSelect.Value("hu").Options(Countries).Label("Country").Clearable(true).ToHtml(),
            StringComparison.Ordinal);

        // Nothing chosen, nothing to clear — and a disabled field is not one to change.
        Assert.DoesNotContain("aria-label=\"Clear Country\"",
            UiSelect.Value(default(string)).Options(Countries).Label("Country").Clearable(true).ToHtml(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Clear Country\"",
            UiSelect.Value("hu").Options(Countries).Label("Country").Clearable(true).Disabled(true).ToHtml(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_commits_nothing_chosen()
    {
        // A bound member is nullable, so the option values are too — clearing has to have a value to
        // commit, and "nothing chosen" is that value.
        (string? Value, string Text)[] countries = [("hu", "Hungary"), ("gb", "United Kingdom")];
        var model = new Trip();
        var page = global::Rask.Testing.Test.Render(
            UiSelect.Bind(() => model.Country).Options(countries).Label("Country").Clearable(true));

        await page.On("button[aria-label=\"Clear Country\"]").ClickAsync();

        Assert.Null(model.Country);
    }

    // The search input's id, which the component derives per instance so two selects on one page cannot
    // aim aria-activedescendant at each other's options.
    private static string SearchId(string html) =>
        System.Text.RegularExpressions.Regex.Match(html, "id=\"([^\"]*-search)\"").Groups[1].Value;

    private sealed class Trip
    {
        public string? Country { get; set; } = "hu";
    }
}
