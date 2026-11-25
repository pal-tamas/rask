using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A live page opened and left alone keeps the one socket it opened: no second one, no close, and no reconnect
///     overlay drawn over the page while the reader looks at it.
/// </summary>
/// <remarks>
///     An app's own suite checked this of its pages after a socket that closed and reopened every few seconds drew the
///     overlay over an idle page. It is the runtime's behaviour, so it is proven here, on a page of Rask's own.
/// </remarks>
public sealed class OneSocketTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // Whether the reconnect overlay was ever shown, watched from before the page loads, so a flash between two looks
    // is seen too.
    private const string WatchOverlay =
        """
        window.__overlayShown = false;
        new MutationObserver(() => {
            if (document.querySelector('.rask-overlay[data-show]')) window.__overlayShown = true;
        }).observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ['data-show'] });
        """;

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task A_live_page_left_alone_keeps_its_one_socket_open_and_shows_no_reconnect_overlay(int width)
    {
        var opened = 0;
        var closed = 0;
        await using var session = await HookSession.OpenAsync<OneSocketPage>(
            playwright,
            new() { ViewportSize = new() { Width = width, Height = 800 } },
            beforeLoad: async page =>
            {
                page.WebSocket += (_, socket) =>
                {
                    Interlocked.Increment(ref opened);
                    socket.Close += (_, _) => Interlocked.Increment(ref closed);
                };
                await page.AddInitScriptAsync(WatchOverlay);
            });
        var page = session.Page;
        await page.ClickAsync("#count");
        await Expect(page.Locator("#counted")).ToHaveTextAsync("1");

        await page.WaitForTimeoutAsync(4000);
        await page.ClickAsync("#count");
        await Expect(page.Locator("#counted")).ToHaveTextAsync("2");

        Assert.Equal(1, Volatile.Read(ref opened));
        Assert.Equal(0, Volatile.Read(ref closed));
        Assert.False(await page.EvaluateAsync<bool>("() => window.__overlayShown"), "the reconnect overlay was drawn over an idle page");
        await Expect(page.Locator(".rask-overlay[data-show]")).ToHaveCountAsync(0);
    }
}

/// <summary>A live page with one handler: a press counts, answered over the page's socket.</summary>
public sealed partial class OneSocketPage : Component
{
    private int _counted;

    protected override Component? HeadAssets => Markup.Title["one socket"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Main[
        P.Id("counted")[_counted.ToString(CultureInfo.InvariantCulture)],
        Button.Id("count").OnClick(() => { _counted++; })["Count"]
    ];
}
