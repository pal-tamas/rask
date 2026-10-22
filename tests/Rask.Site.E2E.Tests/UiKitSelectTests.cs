using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's select on its page in the UI tab, in a browser: the native select, the listbox's keys and placement,
///     searching in the browser and on the server, several answers, the combobox and its create row.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitSelectTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Every_select_example_renders_with_a_real_size() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");

        foreach (var id in new[]
                 {
                     "ui-select", "ui-select-sm", "ui-select-groups", "ui-listbox", "ui-select-button", "ui-select-clearable",
                     "ui-select-prefix", "ui-select-icons", "ui-select-descriptions", "ui-select-avatars", "ui-select-content",
                     "ui-select-width", "ui-select-search", "ui-select-search-slot", "ui-select-keywords", "ui-multiselect",
                     "ui-select-suffix", "ui-select-clear", "ui-combobox", "ui-select-input", "ui-select-backend",
                     "ui-select-store", "ui-select-create", "ui-select-create-search", "ui-select-loading",
                     "ui-select-validation", "ui-select-modal",
                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height.");
        }
    });

    [Fact]
    public Task The_native_select_reports_its_pick() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");

        await Page.Locator("#ui-select-native").SelectOptionAsync("Accounting");

        await Expect(Page.Locator("[data-testid='ui-select-state']")).ToHaveTextAsync("Chosen: Accounting.");
    });

    [Fact]
    public Task The_listbox_opens_walks_and_picks_from_the_keyboard() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var box = Page.Locator("#ui-select-listbox");
        var list = Page.Locator("[data-testid='ui-listbox'] [role='listbox']").First;

        // An arrow opens it, as on Flux; focus stays on the button and aria-activedescendant names the row.
        await box.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(list).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");
        await Page.Keyboard.PressAsync("ArrowDown");
        var active = await box.GetAttributeAsync("aria-activedescendant");
        await Page.Keyboard.PressAsync("Enter");

        Assert.False(string.IsNullOrEmpty(active), "the cursor did not move");
        await Expect(Page.Locator("[data-testid='ui-listbox-state']")).ToHaveTextAsync("Chosen: Design services.");
        await Expect(list).ToBeHiddenAsync();
        await Expect(box).ToBeFocusedAsync();
    });

    [Fact]
    public Task Enter_leaves_a_closed_listbox_shut_and_an_open_one_locks_the_page_behind_it() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var box = Page.Locator("#ui-select-listbox");

        // Flux's button is not a native one: Enter does not press it. An arrow opens it without scrolling the page.
        await box.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
        var before = (await box.BoundingBoxAsync())!.Y;
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");

        Assert.Equal(before, (await box.BoundingBoxAsync())!.Y);
        Assert.Equal("hidden", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overflowY"));
        Assert.Equal("none", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).pointerEvents"));
        await Page.Keyboard.PressAsync("Escape");
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Equal("visible", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overflowY"));
    });

    [Fact]
    public Task Escape_closes_the_listbox_and_leaves_the_focus_on_its_button() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var box = Page.Locator("#ui-select-listbox");

        await box.ClickAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");
        await Page.Keyboard.PressAsync("Escape");

        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(box).ToBeFocusedAsync();
    });

    [Fact]
    public Task The_open_list_hangs_under_its_button_as_wide_as_it_is() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var box = Page.Locator("#ui-select-listbox");
        var list = Page.Locator("[data-testid='ui-listbox'] [data-ui-options]").First;

        await box.ClickAsync();
        await Expect(list).ToBeVisibleAsync();
        var button = await box.BoundingBoxAsync();
        var popup = await list.BoundingBoxAsync();

        // Flux's gap is five pixels; the list lines up with the button's start and takes its width.
        Assert.Equal(button!.X, popup!.X, 1);
        Assert.Equal(button.Width, popup.Width, 1);
        Assert.Equal(button.Y + button.Height + 5, popup.Y, 1);
    });

    [Fact]
    public Task The_searchable_listbox_takes_the_keys_and_filters_as_it_is_typed_into() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var scope = Page.Locator("[data-testid='ui-select-search']");

        await Page.Locator("#ui-select-searchable").ClickAsync();
        await Expect(scope.Locator("[data-ui-select-search] input").First).ToBeFocusedAsync();
        await Page.Keyboard.TypeAsync("leg");
        await Expect(scope.Locator("[data-ui-option]:visible")).ToHaveCountAsync(1);
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page.Locator("[data-testid='ui-select-search-state']")).ToHaveTextAsync("Chosen: Legal services.");
        await Expect(Page.Locator("#ui-select-searchable")).ToBeFocusedAsync();
    });

    [Fact]
    public Task A_search_that_waits_for_a_pause_asks_the_store_once_and_picks_from_its_answer() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var scope = Page.Locator("[data-testid='ui-select-store']");

        await Page.Locator("#ui-select-store").ClickAsync();
        await Expect(scope.Locator("[data-ui-select-search] input")).ToBeFocusedAsync();
        // One `input` for the whole word: how fast a loaded runner types is not what the pause is held to here.
        await scope.Locator("[data-ui-select-search] input").FillAsync("ber");
        await Expect(scope.Locator("[data-ui-option]:visible")).ToHaveTextAsync(["Berlin", "Bern"]);
        await scope.Locator("[data-ui-option]", new LocatorLocatorOptions { HasTextString = "Bern" }).ClickAsync();

        // One query for the word, and one for the empty field the pick left behind it: the page's search follows the field.
        await Expect(Page.Locator("[data-testid='ui-select-store-state']")).ToHaveTextAsync("City: Bern. Queries: 2.");
        await Expect(Page.Locator("#ui-select-store")).ToContainTextAsync("Bern");
    });

    [Fact]
    public Task A_combobox_that_waits_for_a_pause_asks_the_store_once_and_picks_from_its_answer() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var scope = Page.Locator("[data-testid='ui-select-backend']");
        var input = Page.Locator("#ui-select-backend");

        await input.ClickAsync();
        await Expect(scope.Locator("[data-ui-option]:visible")).ToHaveCountAsync(6);
        // One `input` for the whole word: how fast a loaded runner types is not what the pause is held to here.
        await input.FillAsync("an");
        await Expect(scope.Locator("[data-ui-option]:visible")).ToHaveTextAsync(["Branding", "Analytics", "Performance"]);
        await scope.Locator("[data-ui-option]:visible", new LocatorLocatorOptions { HasTextString = "Analytics" }).ClickAsync();

        // The search for "an", then the words the input went on to say: the page's search follows the input.
        await Expect(Page.Locator("[data-testid='ui-select-backend-state']")).ToHaveTextAsync("Project: Analytics. Searches: 2.");
        await Expect(input).ToHaveValueAsync("Analytics");
    });

    [Fact]
    public Task The_multiple_listbox_stays_open_and_counts_its_answers() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var scope = Page.Locator("[data-testid='ui-multiselect']");
        var box = Page.Locator("#ui-select-multiple");

        await box.ClickAsync();
        await scope.Locator("[data-ui-option]", new LocatorLocatorOptions { HasTextString = "Design services" }).ClickAsync();
        await scope.Locator("[data-ui-option]", new LocatorLocatorOptions { HasTextString = "Other" }).ClickAsync();

        await Expect(scope.Locator("[role='listbox']")).ToBeVisibleAsync();
        await Expect(scope.Locator("[role='listbox']")).ToHaveAttributeAsync("aria-multiselectable", "true");
        await Expect(box).ToContainTextAsync("2 selected");
        await Expect(Page.Locator("[data-testid='ui-multiselect-state']")).ToContainTextAsync("Design services, Other");
    });

    [Fact]
    public Task The_combobox_filters_and_shows_its_answer() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var input = Page.Locator("#ui-select-combobox");

        await input.ClickAsync();
        await Page.Keyboard.TypeAsync("de");
        await Expect(Page.Locator("[data-testid='ui-combobox'] [data-ui-option]:visible")).ToHaveCountAsync(2);
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(input).ToHaveValueAsync("Web development");
        await Expect(Page.Locator("[data-testid='ui-combobox-state']")).ToHaveTextAsync("Industry: Web development.");
    });

    [Fact]
    public Task The_combobox_creates_the_option_that_is_not_there() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var input = Page.Locator("#ui-select-create");

        await input.ClickAsync();
        await Page.Keyboard.TypeAsync("Onboarding");
        await Expect(Page.Locator("[data-testid='ui-select-create'] [data-ui-option-create]:visible")).ToHaveCountAsync(1);
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page.Locator("[data-testid='ui-select-create-state']")).ToHaveTextAsync("Project: Onboarding.");
    });
}
