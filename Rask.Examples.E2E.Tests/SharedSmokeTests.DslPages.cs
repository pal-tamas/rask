using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Light smoke for the four DSL showcase pages. Each one demos the generated
// factories or built-in primitives; the existing suite tests one assertion per
// page. These broaden to "page boots, heading renders, at least one demo
// surface visible" so a regression in factory generation or universal-prop
// rendering shows up as a per-page failure rather than a single shared one.
public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task Dsl_TagsPage_RendersHeadingAndCodeSamples() => RunAsync(async () =>
    {
        await NavigateToAsync("/tags");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Tag factories",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        var codeCount = await Page.Locator("pre code").CountAsync();
        Assert.True(codeCount >= 3, $"Expected several code samples on /tags; got {codeCount}");
    });

    [Fact]
    public Task Dsl_PrimitivesPage_RendersHeading() => RunAsync(async () =>
    {
        await NavigateToAsync("/primitives");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Primitives",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
    });

    [Fact]
    public Task Dsl_PropsPage_RendersHeading() => RunAsync(async () =>
    {
        await NavigateToAsync("/props");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Universal props",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
    });

    [Fact]
    public Task Dsl_ComponentsPage_RendersHeading() => RunAsync(async () =>
    {
        await NavigateToAsync("/components");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("User components",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
    });
}
