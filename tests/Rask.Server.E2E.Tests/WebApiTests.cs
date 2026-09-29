using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Web;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a one-element test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     MDN's globals from Rask.Web, called from a server handler: each chain crosses the page's socket and runs in the
///     browser in one round trip, and an object kept there comes back as a handle.
/// </summary>
public sealed class WebApiTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_server_handler_reads_writes_and_keeps_web_objects_in_the_browser()
    {
        await using var host = await LiveServerHost.StartAsync<WebApiPage>(blockWebSockets: false);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        await Expect(page.Locator("#out")).ToHaveTextAsync("-", new() { Timeout = 15_000 });

        await page.Locator("#go").ClickAsync();

        await Expect(page.Locator("#out")).ToHaveTextAsync("stored=from the server; wide=True; kept=True; name=set by Rask.Web", new() { Timeout = 15_000 });
        Assert.Equal("from the server", await page.EvaluateAsync<string>("() => localStorage.getItem('rask-web-e2e')"));
    }
}

public sealed partial class WebApiPage : Component
{
    private string _out = "-";

    protected override Component? HeadAssets => Markup.Title["web api"];
    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("out")[_out],
        Button.Id("go").OnClick(async () =>
        {
            await LocalStorage.SetItem("rask-web-e2e", "from the server");
            var stored = await LocalStorage.GetItem("rask-web-e2e");
            var wide = await Window.MatchMedia("(min-width: 1px)").Matches;
            await using var query = await Window.MatchMedia("(min-width: 1px)");
            var kept = await query.Matches;
            await Window.SetName("set by Rask.Web");
            var name = await Window.Name;
            _out = $"stored={stored}; wide={wide}; kept={kept}; name={name}";
        })["go"]
    ];
}
