using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

[Collection(ServerExampleCollection.Name)]
public sealed partial class ServerExampleTests(ServerExampleAppFixture app, PlaywrightFixture pw) : ExampleSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Server";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public async Task WebSocket_AfterOfflineOnline_PreservesEventsClickState()
    {
        await Page.GotoAsync("/events");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Events",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var clickButton = Page.Locator(".sample-result-body button:has-text('Clicks:')").First;
        await clickButton.ClickAsync();
        await clickButton.ClickAsync();
        await Expect(clickButton).ToContainTextAsync("Clicks: 2",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await Page.Context.SetOfflineAsync(true);
        await Page.Context.SetOfflineAsync(false);

        // Click once after reconnect; the server-held state should still be 2 → 3.
        await clickButton.ClickAsync();
        await Expect(clickButton).ToContainTextAsync("Clicks: 3",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
