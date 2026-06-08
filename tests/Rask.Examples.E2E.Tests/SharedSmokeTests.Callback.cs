using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task Callback_ChildEmit_RerendersParent() => RunAsync(async () =>
    {
        // The child's star button only dirties the child on click, and the child invokes the
        // parent's plain delegate off that path; the parent's rating line updates solely because
        // the framework auto-wraps the delegate to re-render its owner (the parent). If that
        // re-render were broken, the text would stay at "Click a star to rate."
        await NavigateToAsync("/callback");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Callback",
            new() { Timeout = 30_000 });

        var line = Page.Locator("main .sample-result-body p");
        await Expect(line).ToContainTextAsync("Click a star to rate", new() { Timeout = 10_000 });

        // Click the 4th star.
        await Page.Locator("main .sample-result-body button").Nth(3).ClickAsync();
        await Expect(line).ToContainTextAsync("You rated: 4/5", new() { Timeout = 10_000 });
    });
}
