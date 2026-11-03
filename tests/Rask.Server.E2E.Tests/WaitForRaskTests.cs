using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Testing;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>await page.WaitForRask()</c> (Rask.Testing.Playwright) on the Server host: once it returns, what the press, the
///     navigation or the typing caused is on the page, read with no retrying assertion — and a page that pushes a
///     render every second still settles.
/// </summary>
/// <remarks>
///     Every read after a wait is a plain <c>TextContentAsync</c>, never Playwright's retrying <c>Expect</c>: a wait that
///     returned early would fail the read. The slow cases are forced with held replies, not timed. The mark itself is
///     <c>BusyMarkClientTests</c> in <c>Rask.Core.Tests</c>; the WebAssembly host's half is <c>WaitForRaskWasmTests</c>.
/// </remarks>
public sealed class WaitForRaskTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Busy = "() => document.documentElement.hasAttribute('data-rask-busy')";

    [Fact]
    public async Task A_press_marks_the_page_busy_in_the_task_it_was_made_in()
    {
        await using var session = await HookSession.OpenAsync<SettlingPage>(playwright);
        var page = session.Page;
        await page.WaitForRask();

        var markedAtOnce = await page.EvaluateAsync<bool>(
            "() => { document.getElementById('count').click(); return document.documentElement.hasAttribute('data-rask-busy'); }");
        await page.WaitForRask();

        Assert.True(markedAtOnce);
        Assert.Equal("count=1", await page.Locator("#counted").TextContentAsync());
    }

    [Fact]
    public async Task The_wait_holds_while_the_answer_is_held_on_the_line_and_returns_once_it_is_on_the_page()
    {
        await using var session = await HookSession.OpenAsync<SettlingPage>(playwright, beforeLoad: HeldReplies.Install);
        var page = session.Page;
        await page.WaitForRask();
        await HeldReplies.HoldAsync(page);

        await page.ClickAsync("#count");
        var waiting = page.WaitForRask();
        await HeldReplies.UntilAnyHeldAsync(page);
        await page.WaitForTimeoutAsync(300);
        var settledWhileHeld = waiting.IsCompleted;
        var countWhileHeld = await page.Locator("#counted").TextContentAsync();
        await HeldReplies.ReleaseAsync(page);
        await waiting;

        Assert.False(settledWhileHeld);
        Assert.Equal("count=0", countWhileHeld);
        Assert.Equal("count=1", await page.Locator("#counted").TextContentAsync());
    }

    [Fact]
    public async Task A_handler_that_takes_its_time_is_on_the_page_when_the_wait_returns()
    {
        await using var session = await HookSession.OpenAsync<SettlingPage>(playwright);
        var page = session.Page;
        await page.WaitForRask();

        await page.ClickAsync("#ship");
        await page.WaitForRask();

        Assert.Equal("status=shipped", await page.Locator("#status").TextContentAsync());
    }

    [Fact]
    public async Task A_navigation_has_landed_when_the_wait_returns()
    {
        await using var session = await HookSession.OpenAsync<SettlingPage>(playwright, beforeLoad: HeldReplies.Install);
        var page = session.Page;
        await page.WaitForRask();
        await HeldReplies.HoldAsync(page);

        await page.ClickAsync("#to-next");
        var waiting = page.WaitForRask();
        await HeldReplies.UntilAnyHeldAsync(page);
        await page.WaitForTimeoutAsync(300);
        var settledWhileHeld = waiting.IsCompleted;
        await HeldReplies.ReleaseAsync(page);
        await waiting;

        Assert.False(settledWhileHeld);
        Assert.Equal("path=/next", await page.Locator("#where").TextContentAsync());
        Assert.EndsWith("/next", page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deferred_field_and_its_submit_are_both_answered_when_the_wait_returns()
    {
        await using var session = await HookSession.OpenAsync<SettlingPage>(playwright);
        var page = session.Page;
        await page.WaitForRask();

        await page.FillAsync("#name", "Ada");
        await page.ClickAsync("#save");
        await page.WaitForRask();

        Assert.Equal("saved=Ada", await page.Locator("#saved").TextContentAsync());
    }

    [Fact]
    public async Task What_is_typed_into_a_debounced_field_is_sent_and_answered_before_the_wait_returns()
    {
        await using var session = await HookSession.OpenAsync<SettlingPage>(playwright);
        var page = session.Page;
        await page.WaitForRask();

        await page.FillAsync("#search", "tea");
        var markedWhilePaused = await page.EvaluateAsync<bool>(Busy);
        await page.WaitForRask();

        Assert.True(markedWhilePaused);
        Assert.Equal("q=tea", await page.Locator("#echo").TextContentAsync());
    }

    [Fact]
    public async Task A_page_that_pushes_a_render_every_second_still_settles_and_a_press_on_it_is_answered()
    {
        await using var session = await HookSession.OpenAsync<TickingPage>(playwright);
        var page = session.Page;
        await page.WaitForRask(5.Seconds);
        await Assertions.Expect(page.Locator("#ticks")).Not.ToHaveTextAsync("ticks=0");

        await page.ClickAsync("#bell");
        await page.WaitForRask(5.Seconds);
        var opened = await page.Locator("#opened").TextContentAsync();
        await page.WaitForTimeoutAsync(1500);
        await page.WaitForRask(5.Seconds);

        Assert.Equal("opened=1", opened);
    }
}

/// <summary>A button answered at once, one answered slowly, a link, a deferred form and a debounced search.</summary>
public sealed partial class SettlingPage(RouteState route) : Component
{
    private readonly SettlingForm _form = new();
    private int _count;
    private string _status = "new";
    private string _saved = "";

    protected override Component? HeadAssets => Markup.Title["settling"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("where")[$"path={route.Path}"],
        P.Id("counted")[$"count={_count}"],
        P.Id("status")[$"status={_status}"],
        P.Id("saved")[$"saved={_saved}"],
        P.Id("echo")[$"q={_form.Q}"],
        Button.Id("count").OnClick(() => _count++)["count"],
        Button.Id("ship").OnClick(Ship)["ship"],
        NavLink.Href("/next").Id("to-next")["next"],
        Form.Model(_form).OnSubmit(form => _saved = form.Name).NoValidate(true)[
            Input.Bind(() => _form.Name).Id("name"),
            Button.Type(ButtonType.Submit).Id("save")["save"]
        ],
        Input.Bind(() => _form.Q).Debounce(800.Milliseconds).Id("search")
    ];

    private async Task Ship()
    {
        await Task.Delay(400);
        _status = "shipped";
    }
}

/// <summary>What the settling page's fields are bound to.</summary>
public sealed class SettlingForm
{
    public string Name { get; set; } = "";

    public string Q { get; set; } = "";
}

/// <summary>A page the server renders again every second on its own, with a bell on it: a notification centre.</summary>
public sealed partial class TickingPage : Component
{
    private Timer? _timer;
    private int _ticks;
    private int _opened;

    protected override Component? HeadAssets => Markup.Title["ticking"];

    protected override string? HtmlLang => "en";

    protected override Task OnMount()
    {
        _timer = new Timer(_ =>
        {
            _ticks++;
            StateHasChanged();
        }, null, 1000, 1000);
        return Task.CompletedTask;
    }

    protected override async Task OnUnmount()
    {
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
        }
    }

    protected override Component? Render() =>
    [
        P.Id("ticks")[$"ticks={_ticks}"],
        P.Id("opened")[$"opened={_opened}"],
        Button.Id("bell").OnClick(() => _opened++)["bell"]
    ];
}
