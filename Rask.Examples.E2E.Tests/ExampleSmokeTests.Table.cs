using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

public abstract partial class ExampleSmokeTests
{
    [Fact]
    public Task Table_DeepLink_AllParams_AppliesAndRenders() => RunAsync(async () =>
    {
        // Deep link with every supported query param baked in. The page binds
        // all 5 via [QueryParam] and the resulting view must match: filter
        // "L" restricts rows, sort=name dir=desc orders descending, size=5
        // limits to 5 rows on page 1.
        await Page.GotoAsync("/table?filter=L&sort=name&dir=desc&page=1&size=5");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Data table",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // 5 rows at most (could be fewer if "L" matches < 5 rows).
        var rows = await Page.Locator("tbody tr").CountAsync();
        Assert.True(rows >= 1 && rows <= 5, $"Expected 1-5 rows on filtered page; got {rows}");

        // Footer mentions filtered count.
        await Expect(Page.Locator("small.text-secondary").First)
            .ToContainTextAsync("filtered from 120",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
    });
}
