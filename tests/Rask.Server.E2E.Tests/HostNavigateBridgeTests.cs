using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>__raskHost.navigate</c> on the Server host: the bridge front-end code navigates in-app through, driven in a real browser over the live socket.
/// </summary>
/// <remarks>
///     The WASM host's bridge is pinned under node (<c>HostNavigateTests</c> in Rask.Wasm.Tests); this bundle is not run
///     under node by convention, so its three claims are made here instead: an in-app URL navigates without a reload,
///     a replacing one takes the current history entry's place, and another origin's is refused out loud.
/// </remarks>
public sealed class HostNavigateBridgeTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task Front_end_code_navigates_through_the_bridge_without_a_reload()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var entries = await page.EvaluateAsync<int>("() => history.length");

        await page.EvaluateAsync("() => window.__raskHost.navigate('/bridge/arrived?from=code')");

        await Expect(page.Locator("#arrived")).ToHaveTextAsync("arrived");
        Assert.Equal(session.BaseUrl + "/bridge/arrived?from=code", page.Url);
        Assert.Equal("kept", await page.EvaluateAsync<string>("() => window.raskProbe"));
        Assert.Equal(entries + 1, await page.EvaluateAsync<int>("() => history.length"));
    }

    [Fact]
    public async Task A_replacing_navigation_through_the_bridge_takes_the_current_entrys_place()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var entries = await page.EvaluateAsync<int>("() => history.length");

        await page.EvaluateAsync("() => window.__raskHost.navigate('/bridge/arrived', true)");

        await Expect(page.Locator("#arrived")).ToHaveTextAsync("arrived");
        Assert.Equal(session.BaseUrl + "/bridge/arrived", page.Url);
        Assert.Equal(entries, await page.EvaluateAsync<int>("() => history.length"));
    }

    [Fact]
    public async Task A_url_of_another_origin_is_refused_by_the_bridge_and_says_so()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var said = page.WaitForConsoleMessageAsync(new() { Predicate = message => message.Type == "error" });

        await page.EvaluateAsync("() => window.__raskHost.navigate('https://elsewhere.example/orders')");

        Assert.Contains("\"https://elsewhere.example/orders\" is not a URL of this app", (await said).Text, StringComparison.Ordinal);
        await Expect(page.Locator("#start")).ToHaveTextAsync("start");
        Assert.Equal(session.BaseUrl + "/bridge", page.Url);
    }

    // The start page, with the runtime's bridge in place and a probe a reload would lose.
    private async Task<HookSession> OpenAsync()
    {
        var session = await HookSession.OpenAsync<BridgeApp>(playwright, path: "/bridge");
        try
        {
            await Expect(session.Page.Locator("#start")).ToHaveTextAsync("start");
            await session.Page.WaitForFunctionAsync("() => typeof window.__raskHost?.navigate === 'function'");
            await session.Page.EvaluateAsync("() => { window.raskProbe = 'kept'; }");
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }
}

public sealed partial class BridgeApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/bridge/arrived")]
public sealed partial class BridgeArrivedPage : Component
{
    protected override Component? Render() => P.Id("arrived")["arrived"];
}

[Route("/bridge")]
public sealed partial class BridgeStartPage : Component
{
    protected override Component? Render() => P.Id("start")["start"];
}
