using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // small test pages; their <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Lazy.Placeholder(…)[Widget]</c> on a live Server page: the first response carries the placeholder, the
///     widget is mounted only once it is scrolled into view — one frame, nobody's action — and a lazy inside it
///     waits for its own turn. Livewire's lazy component.
/// </summary>
public sealed class LazyTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task The_first_response_carries_the_placeholder_and_nothing_of_the_widget()
    {
        await using var session = await HookSession.OpenAsync<LazyPage>(playwright);
        using var http = new HttpClient();

        var html = await http.GetStringAsync(session.BaseUrl + "/", TestContext.Current.CancellationToken);

        Assert.Contains("data-rask-lazy", html, StringComparison.Ordinal);
        Assert.Contains("Loading sales", html, StringComparison.Ordinal);
        Assert.DoesNotContain("mounted=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_widget_mounts_once_when_scrolled_into_view_for_one_frame()
    {
        await using var session = await HookSession.OpenAsync<LazyPage>(playwright, beforeLoad: CountFrames);
        var page = session.Page;
        await Ready(page);
        await page.WaitForTimeoutAsync(400);
        var beforeScrolling = await page.Locator("#sales").CountAsync();
        var sent = await SentAsync(page);
        var mounts = LazySalesWidget.Mounts;

        await page.Locator("#sales-placeholder").ScrollIntoViewIfNeededAsync();
        await Expect(page.Locator("#sales")).ToHaveTextAsync("mounted=1");
        await page.WaitForTimeoutAsync(400);

        Assert.Equal(0, beforeScrolling);
        Assert.Equal(1, await SentAsync(page) - sent);
        Assert.Equal(1, LazySalesWidget.Mounts - mounts);
        await Expect(page.Locator("#sales")).ToHaveTextAsync("mounted=1");
    }

    [Fact]
    public async Task A_lazy_inside_a_loaded_one_waits_until_it_is_in_view_itself()
    {
        await using var session = await HookSession.OpenAsync<LazyPage>(playwright);
        var page = session.Page;
        await Ready(page);

        await page.Locator("#sales-placeholder").ScrollIntoViewIfNeededAsync();
        await Expect(page.Locator("#sales")).ToHaveTextAsync("mounted=1");
        var innerBeforeScrolling = await page.Locator("#details").CountAsync();
        await page.Locator("#details-placeholder").ScrollIntoViewIfNeededAsync();

        await Expect(page.Locator("#details")).ToHaveTextAsync("details");
        Assert.Equal(0, innerBeforeScrolling);
    }

    private static Task CountFrames(IPage page) => page.AddInitScriptAsync(
        """
        window.__sent = 0;
        const send = WebSocket.prototype.send;
        WebSocket.prototype.send = function (data) {
            const frame = JSON.parse(data);
            window.__sent += frame.type === "batch" ? frame.events.length : 1;
            return send.call(this, data);
        };
        """);

    private static Task<int> SentAsync(IPage page) => page.EvaluateAsync<int>("() => window.__sent");

    private static async Task Ready(IPage page)
    {
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");
    }
}

/// <summary>A sales widget below the fold, behind a lazy, with a lazy of its own further down.</summary>
public sealed partial class LazyPage : Component
{
    private bool _ready;

    protected override Component? HeadAssets => Markup.Title["lazy"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("is-ready")[_ready ? "ready" : "loading"],
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        Div.Style("height:2000px"),
        Lazy.Placeholder(P.Id("sales-placeholder")["Loading sales"])[LazySalesWidget]
    ];
}

/// <summary>Counts its mounts, and holds a lazy of its own below another fold.</summary>
public sealed partial class LazySalesWidget : Component
{
    private int _mounts;

    /// <summary>Every mount of every instance, for the test that asks how many there were.</summary>
    internal static int Mounts => _all;

    private static int _all;

    protected override Task OnMount()
    {
        _mounts++;
        Interlocked.Increment(ref _all);
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        P.Id("sales")[$"mounted={_mounts}"],
        Div.Style("height:2000px"),
        Lazy.Placeholder(P.Id("details-placeholder")["Loading details"])[P.Id("details")["details"]]
    ];
}
