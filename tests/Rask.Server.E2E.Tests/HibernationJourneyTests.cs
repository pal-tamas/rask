using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Server.Hibernation;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A tab out of sight long enough lets its session go, and gets its page back — state and all — when it is looked
///     at again (#1322). At a desktop and a phone width.
/// </summary>
/// <remarks>
///     The tab is hidden the way the runtime hears it: <c>document.visibilityState</c> reads <c>hidden</c> and a
///     <c>visibilitychange</c> is raised. A headless browser keeps every page in sight, so the state is put on the
///     document rather than reached by switching tabs. The host's timings are shortened so a round runs every 200 ms
///     and a hidden tab is due at once.
/// </remarks>
public sealed class HibernationJourneyTests(PlaywrightFixture playwright, ITestOutputHelper output)
    : IClassFixture<PlaywrightFixture>
{
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 20_000 };

    private static readonly SessionHibernationOptions AtOnce = new()
    {
        HiddenFor = TimeSpan.Zero,
        Every = TimeSpan.FromMilliseconds(200),
        AskAgainAfter = TimeSpan.FromMilliseconds(200),
    };

    private const string Hide = """
        () => {
            Object.defineProperty(document, "visibilityState", {configurable: true, get: () => "hidden"});
            Object.defineProperty(document, "hidden", {configurable: true, get: () => true});
            document.dispatchEvent(new Event("visibilitychange"));
        }
        """;

    private const string Show = """
        () => {
            Object.defineProperty(document, "visibilityState", {configurable: true, get: () => "visible"});
            Object.defineProperty(document, "hidden", {configurable: true, get: () => false});
            document.dispatchEvent(new Event("visibilitychange"));
        }
        """;

    // What the server holds is not on the page, so it is polled — for as long as a page assertion would wait.
    private static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "never happened: " + what);
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }
    }

    private static Task<LiveServerHost> StartAsync() =>
        LiveServerHost.StartAsync<HibernatingCounterPage>(
            blockWebSockets: false, services: s => s.AddSingleton(AtOnce));

    private async Task<IPage> OpenAsync(LiveServerHost host, int width)
    {
        var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 800 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        await page.WaitForFunctionAsync("() => document.body.inert === false");
        return page;
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task A_hidden_tab_lets_its_session_go_and_comes_back_with_its_state_when_shown(int width)
    {
        await using var host = await StartAsync();
        var store = host.Services.GetRequiredService<LiveSessionStore>();
        var page = await OpenAsync(host, width);
        await page.ClickAsync("#bump");
        await Expect(page.Locator("#count")).ToHaveTextAsync("count=1", Settled);
        await page.ClickAsync("#bump");
        await Expect(page.Locator("#count")).ToHaveTextAsync("count=2", Settled);
        var firstSession = await page.GetAttributeAsync("[data-rask-root]", "data-rask-root");

        await page.EvaluateAsync(Hide);
        await Until(() => store.Count == 0, "the server lets the hidden tab's session go");
        var whileAway = await page.TextContentAsync("#count");
        var overlayWhileAway = await page.EvaluateAsync<bool>(
            "() => !!document.querySelector('.rask-overlay[data-show]')");
        var shown = Stopwatch.StartNew();
        await page.EvaluateAsync(Show);
        await page.WaitForFunctionAsync(
            "first => document.querySelector('[data-rask-root]').getAttribute('data-rask-root') !== first "
            + "&& document.body.inert === false",
            firstSession);
        shown.Stop();
        var countBack = await page.TextContentAsync("#count");
        await page.ClickAsync("#bump");

        Assert.Equal("count=2", whileAway);
        Assert.False(overlayWhileAway, "nothing is shown while the tab sleeps");
        Assert.Equal("count=2", countBack);
        await Expect(page.Locator("#count")).ToHaveTextAsync("count=3", Settled);
        Assert.Equal(1, store.Count);
        output.WriteLine($"width {width}: shown to rebuilt page in {shown.Elapsed.TotalMilliseconds:0} ms");
        await page.Context.CloseAsync();
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task A_hidden_tab_holding_typed_text_not_yet_sent_keeps_its_session(int width)
    {
        await using var host = await StartAsync();
        var store = host.Services.GetRequiredService<LiveSessionStore>();
        var page = await OpenAsync(host, width);
        await page.FillAsync("#draft", "half a sentence");
        var session = store.Snapshot().Single();

        await page.EvaluateAsync(Hide);
        await Until(
            () => session.Hibernation.AskedAt != 0 && !session.Hibernation.Offered, "the tab is asked and says not now");
        await Task.Delay(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);
        var held = store.Count;
        await page.EvaluateAsync(Show);
        await page.ClickAsync("#bump");

        Assert.Equal(1, held);
        Assert.Same(session, store.Snapshot().Single());
        Assert.Equal("half a sentence", await page.InputValueAsync("#draft"));
        await Expect(page.Locator("#typed")).ToHaveTextAsync("typed=half a sentence", Settled);
        await page.Context.CloseAsync();
    }
}

/// <summary>A counter that declares its count, so a page rebuilt from its record shows it, and a field that waits.</summary>
public sealed partial class HibernatingCounterPage(IPersistentState state) : Component
{
    private int _count;
    private readonly HibernationDraft _draft = new();

    protected override Component? HeadAssets => Markup.Title["hibernating counter"];
    protected override string? HtmlLang => "en";

    protected override Task OnMount()
    {
        state.TryGet(nameof(_count), out _count);
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        P.Id("count")[$"count={_count}"],
        Button.Id("bump").OnClick(Bump)["bump"],
        P.Id("typed")[$"typed={_draft.Text}"],
        Form.Model(_draft).NoValidate(true)[
            Input.Bind(() => _draft.Text).Id("draft")
        ]
    ];

    private void Bump()
    {
        _count++;
        state.Persist(nameof(_count), _count);
    }
}

/// <summary>What the page's one field is bound to.</summary>
public sealed class HibernationDraft
{
    public string Text { get; set; } = "";
}
