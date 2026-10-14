using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The reconnect overlay speaks the page's language, and <c>&lt;html&gt;</c> says <c>data-rask-offline</c> while
///     the page cannot reach its app — Livewire's <c>wire:offline</c>, as a variant a page can style.
/// </summary>
/// <remarks>
///     A drop is staged the way a lost network looks from the page: the browser goes offline and the open socket
///     closes, so the reconnect that follows fails. The browser-offline half on WebAssembly is
///     <c>OfflineMarkWasmTests</c> in <c>Rask.Site.E2E.Tests</c>.
/// </remarks>
public sealed class OfflineAndReconnectTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Remember =
        "() => { window.__sockets = []; const Native = window.WebSocket; "
        + "window.WebSocket = function (...args) { const s = new Native(...args); window.__sockets.push(s); return s; }; "
        + "window.WebSocket.prototype = Native.prototype; Object.assign(window.WebSocket, Native); }";

    [Fact]
    public async Task A_hungarian_page_says_it_is_offline_in_hungarian_and_marks_html_while_the_connection_is_down()
    {
        await using var session = await HookSession.OpenAsync<OfflinePage>(
            playwright, new() { Locale = "hu-HU", ViewportSize = new() { Width = 1000, Height = 700 } },
            beforeLoad: page => page.AddInitScriptAsync("(" + Remember + ")()"), cultures: o => o.SupportedCultures.Add("hu"));
        var page = session.Page;
        await page.WaitForFunctionAsync("() => document.body.inert === false && window.__sockets.length > 0");

        await page.Context.SetOfflineAsync(true);
        await page.EvaluateAsync("() => window.__sockets.forEach(s => s.close())");

        await Expect(page.Locator(".rask-overlay__msg")).ToHaveTextAsync(
            "Nincs internetkapcsolat — várakozás az újracsatlakozásra…", new() { Timeout = 15_000 });
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-rask-offline", "");
        await Expect(page.Locator(".rask-overlay__retry")).ToHaveTextAsync("Újrapróbálás most");
    }

    [Fact]
    public async Task The_mark_goes_when_the_connection_is_back()
    {
        await using var session = await HookSession.OpenAsync<OfflinePage>(
            playwright, beforeLoad: page => page.AddInitScriptAsync("(" + Remember + ")()"));
        var page = session.Page;
        await page.WaitForFunctionAsync("() => document.body.inert === false && window.__sockets.length > 0");

        await page.Context.SetOfflineAsync(true);
        await page.EvaluateAsync("() => window.__sockets.forEach(s => s.close())");
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-rask-offline", "");
        await page.Context.SetOfflineAsync(false);

        await Expect(page.Locator("html")).Not.ToHaveAttributeAsync("data-rask-offline", new System.Text.RegularExpressions.Regex(".*"), new() { Timeout = 20_000 });
        await Expect(page.Locator("#hello")).ToHaveTextAsync("hello");
    }

    [Fact]
    public async Task An_english_page_keeps_the_english_words_and_an_online_page_carries_no_mark()
    {
        await using var session = await HookSession.OpenAsync<OfflinePage>(playwright);
        var page = session.Page;

        var texts = await page.EvaluateAsync<string[]>("() => JSON.parse(document.getElementById('rask-texts').textContent)");

        Assert.Equal("Reconnecting…", texts[0]);
        Assert.Equal("Your session timed out. Reload to continue.", texts[7]);
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.hasAttribute('data-rask-offline')"));
    }
}

/// <summary>A page that only has to be connected — with a kit button on it, which is what brings the kit's Hungarian along.</summary>
public sealed partial class OfflinePage : Component
{
    protected override Component? HeadAssets => Markup.Title["offline"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => [P.Id("hello")["hello"], Ui.Button.Id("kit")["kit"]];
}
