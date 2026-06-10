using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task ElementRef_FocusButton_FocusesInputViaJsInterop() => RunAsync(async () =>
    {
        // End-to-end of the ref path: ElementRef -> data-rask-ref -> InvokeVoidAsync -> the
        // client reviver resolves it to the element -> __raskEl.focus(el). If any link were
        // broken the input would never gain focus.
        await NavigateToAsync("/element-ref");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Element refs",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var input = Page.Locator("main .sample-result-body input");
        await Expect(input).Not.ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });

        await Page.Locator("main .sample-result-body button:has-text('Focus the input')").ClickAsync();
        await Expect(input).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task ElementRef_MeasureButton_PassesElementToScopedJs() => RunAsync(async () =>
    {
        // The box's ref is handed to user scoped JS (Rask.ElementRefDemo.width), which receives
        // the resolved element and returns its width — proving refs reach user JS, not just builtins.
        await NavigateToAsync("/element-ref");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Element refs",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("main .sample-result-body button:has-text('Measure the box')").ClickAsync();
        await Expect(Page.Locator("main .sample-result-body p"))
            .ToContainTextAsync("Box width:", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });
}
