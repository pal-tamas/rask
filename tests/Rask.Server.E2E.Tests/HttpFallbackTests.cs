using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a one-element test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A network that refuses WebSockets, in a real browser: the page must still work, over plain HTTP, and the tab
///     must stop asking for a socket it cannot have.
/// </summary>
/// <remarks>
///     The unit suites prove each half — the endpoints over an in-memory server, the chooser in node over a fake
///     clock. Neither proves the halves meet: that the runtime a browser actually loads notices the refusal, opens
///     the stream, sends a click the server applies, and reloads straight onto HTTP. That is only true in a browser.
/// </remarks>
public sealed class HttpFallbackTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 15_000 };

    [Fact]
    public async Task A_page_whose_socket_is_refused_works_over_http_and_the_tab_remembers_it()
    {
        await using var host = await LiveServerHost.StartAsync<CounterPage>(blockWebSockets: true);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(host.BaseUrl + "/");

        // The click is the proof, not the storage: it has to travel up as a POST and come back down the stream.
        await page.ClickAsync("#bump");
        await Expect(page.Locator("#count")).ToHaveTextAsync("count=1", Settled);
        Assert.Equal("http", await page.EvaluateAsync<string?>("() => sessionStorage.getItem('rask:transport')"));
        Assert.True(host.WebSocketAttempts >= 1, "the runtime should have tried the socket before falling back");

        // Reloaded, the tab goes straight to HTTP: the socket it already knows it cannot have is not asked for again.
        var attemptsBeforeReload = host.WebSocketAttempts;
        await page.ReloadAsync();
        await Expect(page.Locator("#count")).ToHaveTextAsync("count=0", Settled);
        await page.ClickAsync("#bump");
        await Expect(page.Locator("#count")).ToHaveTextAsync("count=1", Settled);
        Assert.Equal(attemptsBeforeReload, host.WebSocketAttempts);
    }

    [Fact]
    public async Task A_page_whose_socket_opens_stays_on_the_socket()
    {
        await using var host = await LiveServerHost.StartAsync<CounterPage>(blockWebSockets: false);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(host.BaseUrl + "/");
        await page.ClickAsync("#bump");
        await Expect(page.Locator("#count")).ToHaveTextAsync("count=1", Settled);

        // Nothing learned, nothing remembered: the fallback is for networks that refuse sockets, not a preference.
        Assert.Null(await page.EvaluateAsync<string?>("() => sessionStorage.getItem('rask:transport')"));
        Assert.Equal(1, host.WebSocketAttempts);
    }
}

/// <summary>A counter: one handler, one piece of state, both visible to a selector.</summary>
public sealed partial class CounterPage : Component
{
    private int _count;

    protected override Component? HeadAssets => new Title()["counter"];
    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("count")[$"count={_count}"],
        Button.Id("bump").OnClick(() => _count++)["bump"]
    ];
}
