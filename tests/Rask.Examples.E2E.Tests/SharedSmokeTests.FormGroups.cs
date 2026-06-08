using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task FormGroups_RadioAndCheckbox_UpdateBoundModel() => RunAsync(async () =>
    {
        await NavigateToAsync("/form-groups");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Radio & checkbox groups",
            new() { Timeout = 30_000 });

        var summary = Page.Locator("#groups-summary");
        await Expect(summary).ToContainTextAsync("Plan: Free", new() { Timeout = 10_000 });
        await Expect(summary).ToContainTextAsync("Interests: none");

        // Pick the Pro radio (single-value bind).
        await Page.Locator("input[type=radio][value='Pro']").CheckAsync();
        await Expect(summary).ToContainTextAsync("Plan: Pro", new() { Timeout = 10_000 });

        // Tick two interest checkboxes (collection bind).
        await Page.Locator("input[type=checkbox][value='Web']").CheckAsync();
        await Page.Locator("input[type=checkbox][value='AI']").CheckAsync();
        await Expect(summary).ToContainTextAsync("Web", new() { Timeout = 10_000 });
        await Expect(summary).ToContainTextAsync("AI");

        // Untick one — it leaves the collection.
        await Page.Locator("input[type=checkbox][value='Web']").UncheckAsync();
        await Expect(summary).Not.ToContainTextAsync("Web", new() { Timeout = 10_000 });
        await Expect(summary).ToContainTextAsync("AI");
    });
}
