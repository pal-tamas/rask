using System.Text.RegularExpressions;
using Rask.Examples.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

[Collection(ServerExampleCollection.Name)]
public sealed class ServerExampleTests(ServerExampleAppFixture app, PlaywrightFixture pw) : ExampleSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Server";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public async Task WebSocket_AfterContextOfflineThenOnline_PreservesCounterState()
    {
        await Page.GotoAsync("/counter");
        await Expect(Page.Locator("p.fs-5")).ToHaveTextAsync("Current count: 0");

        var btn = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Click me" });
        await btn.ClickAsync();
        await btn.ClickAsync();
        await Expect(Page.Locator("p.fs-5")).ToHaveTextAsync("Current count: 2");

        await Page.Context.SetOfflineAsync(true);
        await Page.Context.SetOfflineAsync(false);

        // Click once after reconnect; the server-held state should still be 2 → 3.
        await btn.ClickAsync();
        await Expect(Page.Locator("p.fs-5"))
            .ToHaveTextAsync("Current count: 3", new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
    }
}
