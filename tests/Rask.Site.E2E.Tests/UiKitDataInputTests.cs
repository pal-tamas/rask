using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's Data input components, in a browser.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitDataInputTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task EveryDataInputComponentRendersWithARealSize() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-text-controls", "ui-labels", "ui-choices", "ui-range", "ui-otp", "ui-filter",
                     "ui-calendar", "ui-bound", "ui-mask",
                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height.");
        }
    });

    [Fact]
    public Task TypingIntoAFieldReachesCSharpAndComesBack() => RunAsync(async () =>
    {
        await OpenAsync();

        var email = Page.Locator("[data-testid='ui-text-controls'] input[type='email']");
        await email.FillAsync("not-an-address");
        await email.BlurAsync();

        // The validator only exists while the value is bad, so its appearance is the proof the value
        // reached C#, was judged there, and came back as different markup.
        await Expect(Page.Locator(".validator-hint")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(email).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("input-error"));

        await email.FillAsync("ada@example.com");
        await email.BlurAsync();
        await Expect(Page.Locator(".validator-hint")).ToHaveCountAsync(0);
    });

    [Fact]
    public Task TheOneTimeCodeIsASingleFieldThatTakesAPastedCode() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-otp']");
        var field = scope.Locator("input.otp");

        // One input, not six. This is what makes a pasted or autofilled code land correctly instead of
        // dropping the whole string into the first box.
        await Expect(scope.Locator("input")).ToHaveCountAsync(1);

        await field.FillAsync("123456");
        await field.BlurAsync();
        await Expect(Page.Locator("[data-testid='ui-otp-state']")).ToContainTextAsync("Code complete");
    });

    [Fact]
    public Task TheFilterNarrowsAndResets() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-filter']");
        var state = Page.Locator("[data-testid='ui-filter-state']");

        await Expect(state).ToContainTextAsync("Showing everything");

        await scope.Locator("input[value='feature']").CheckAsync();
        await Expect(state).ToContainTextAsync("Filtered to feature");

        await scope.Locator(".filter-reset").CheckAsync();
        await Expect(state).ToContainTextAsync("Showing everything");
    });

    [Fact]
    public Task TheCalendarChangesMonthAndPicksADay() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-calendar']");
        var state = Page.Locator("[data-testid='ui-calendar-state']");

        await Expect(state).ToContainTextAsync("No date chosen");

        // Paging months is an ordinary re-render — there is no web component here to ask.
        var heading = scope.Locator(".text-sm.font-semibold").First;
        var before = await heading.TextContentAsync();
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Next month" })
            .ClickAsync();
        await Expect(heading).Not.ToHaveTextAsync(before ?? "");

        await scope.Locator("table button:not([disabled])").First.ClickAsync();
        await Expect(state).ToContainTextAsync("Chosen:");
    });

    [Fact]
    public Task ABoundControlWritesStraightToTheModel() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-bound']");
        var state = Page.Locator("[data-testid='ui-bound-state']");

        // No OnChange anywhere in this section: every one of these writes back through Bind. The
        // markup assertions in Rask.Ui.Tests can only see what a control DRAWS — this is the half that
        // proves the write-back reaches the model over a live session.
        await scope.Locator("input[type='email']").FillAsync("ada@example.com");
        await scope.Locator("input[type='email']").BlurAsync();
        await Expect(state).ToContainTextAsync("ada@example.com");

        // T comes off the model, so a bound int is a number field with nothing said at the call site.
        var seats = scope.Locator("input[type='number']");
        await Expect(seats).ToHaveCountAsync(1);
        await seats.FillAsync("4");
        await seats.BlurAsync();
        await Expect(state).ToContainTextAsync("4 seats");

        await scope.Locator("input.checkbox").CheckAsync();
        await Expect(state).ToContainTextAsync("agreed yes");

        // A rating is radios sharing a name, and a bound radio's state is `checked` rather than a
        // value attribute — which is exactly what used to be wrong.
        await scope.Locator("input.mask-star-2").Nth(2).CheckAsync();
        await Expect(state).ToContainTextAsync("3 stars");
    });

    [Fact]
    public Task TheRangeReportsItsValue() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-range']");

        await Expect(scope).ToContainTextAsync("Volume: 40");

        // A slider that draws a value and reports nothing is one you can push and cannot read; UiRange
        // had no OnChange at all before this.
        await scope.Locator("input[type='range']").FillAsync("75");
        await Expect(scope).ToContainTextAsync("Volume: 75");
    });

    [Fact]
    public Task TheDrawnSelectIsAFullKeyboardCombobox() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-select']");
        var box = scope.GetByRole(AriaRole.Combobox);
        var list = scope.Locator("[role='listbox']");

        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(list).ToBeHiddenAsync();

        // Enter opens it through the button's own activation — that is the keyboard's way in, and it
        // costs no script.
        await box.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(list).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");

        // The roving cursor: focus stays on the box and aria-activedescendant names the option, which
        // is what keeps this free of any focus-moving JS interop.
        await Page.Keyboard.PressAsync("ArrowDown");
        var active = await box.GetAttributeAsync("aria-activedescendant");
        Assert.False(string.IsNullOrEmpty(active), "the cursor did not move");

        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator("[data-testid='ui-select-state']")).ToContainTextAsync("Chosen:");
    });

    [Fact]
    public Task TheDrawnMultiSelectKeepsItsListOpenAcrossSeveralPicks() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-multiselect']");
        var box = scope.GetByRole(AriaRole.Combobox);
        var list = scope.Locator("[role='listbox']");
        var state = Page.Locator("[data-testid='ui-multiselect-state']");

        await box.ClickAsync();
        await Expect(list).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // The whole point of the control, and the one thing a single-select cannot do: a pick does not
        // dismiss the list, so a second answer costs one click rather than another trip through the box.
        await list.GetByRole(AriaRole.Option, new LocatorGetByRoleOptions { Name = "Rask.Cli" })
            .ClickAsync();
        await Expect(list).ToBeVisibleAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");

        await list.GetByRole(AriaRole.Option, new LocatorGetByRoleOptions { Name = "Rask.External" })
            .ClickAsync();
        await Expect(list).ToBeVisibleAsync();
        await Expect(state).ToContainTextAsync("cli");
        await Expect(state).ToContainTextAsync("ext");

        // And the browser still owns dismissal, exactly as it does for the single-select.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(list).ToBeHiddenAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
    });

    [Fact]
    public Task AMultiSelectChipRemovesItsOwnAnswer() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-multiselect']");
        var state = Page.Locator("[data-testid='ui-multiselect-state']");

        // The demo starts with two answers, so a chip is on screen before anything is clicked.
        await Expect(state).ToContainTextAsync("core");

        // Removing from the BOX, without opening the list at all — the affordance the chips exist for.
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove Rask.Core" })
            .ClickAsync();

        await Expect(state).Not.ToContainTextAsync("core");
        await Expect(state).ToContainTextAsync("ui");
        await Expect(scope.Locator("[role='listbox']")).ToBeHiddenAsync();
    });

    [Fact]
    public Task TheMultiSelectListSaysItTakesMoreThanOneAnswer() => RunAsync(async () =>
    {
        await OpenAsync();

        // Not decoration: the options carry aria-selected either way, so without this a reader has no
        // way to learn that a second one is allowed.
        await Expect(Page.Locator("[data-testid='ui-multiselect'] [role='listbox']"))
            .ToHaveAttributeAsync("aria-multiselectable", "true");
    });

    [Fact]
    public Task EscapeClosesTheDrawnSelectAndCSharpHearsIt() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-select']");
        var box = scope.GetByRole(AriaRole.Combobox);

        await box.ClickAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");

        // The assertion the whole toggle event exists for. The BROWSER closes the popover here; without
        // hearing that, aria-expanded would go on claiming the list is open over a closed one.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(scope.Locator("[role='listbox']")).ToBeHiddenAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
    });

    [Fact]
    public Task ArrowKeysInTheDrawnSelectDoNotScrollThePage() => RunAsync(async () =>
    {
        await OpenAsync();

        var box = Page.Locator("[data-testid='ui-select']").GetByRole(AriaRole.Combobox);
        await box.ClickAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");

        var before = await Page.EvaluateAsync<int>("() => window.scrollY");
        for (var i = 0; i < 5; i++)
        {
            await Page.Keyboard.PressAsync("ArrowDown");
        }

        // The client never preventDefaults on its own, so without the containment added to rask-dom.ts
        // every ArrowDown would scroll the document behind the open list.
        var after = await Page.EvaluateAsync<int>("() => window.scrollY");
        Assert.Equal(before, after);
    });

    [Fact]
    public Task TheDrawnListEscapesAnOverflowHiddenAncestor() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-select']");
        var box = scope.GetByRole(AriaRole.Combobox);
        await box.ClickAsync();

        var list = scope.Locator("[role='listbox']");
        await Expect(list).ToBeVisibleAsync();

        // The reason the popover is worth its cost: the box sits in a 96px overflow:hidden container, so
        // a list positioned inside the flow would be clipped to nothing. In the top layer it is not.
        var height = (await list.BoundingBoxAsync())!.Height;
        Assert.True(height > 96, $"the list was clipped to {height}px by its overflow-hidden ancestor.");
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Data input");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data input",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
