using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The runtime runs a document once, however a reader moves through the app, and a list of rows with
///     tooltip buttons raises no error on the way.
/// </summary>
/// <remarks>
///     <para>
///         The runtime's <c>&lt;script&gt;</c> is the last child of the render root. A navigation to a page with
///         one top-level node more or fewer pairs it against something else, and the morph then inserts the
///         incoming tag as a new node. Revived like any other script, that ran the whole runtime again: the
///         second copy declined to boot, but its shared modules had already bound their listeners, with no
///         host behind them, and each keystroke in a bound field threw <c>inRoot() was called before a host
///         was installed</c> from then on. Nothing on the page showed it.
///     </para>
///     <para>
///         <see cref="CountRunsAsync" /> counts the runs where the runtime itself decides whether to boot, so a
///         tag that is merely put back is not mistaken for one that ran.
///     </para>
/// </remarks>
public sealed class RuntimeRunsOnceTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string HoldMs = "700";
    private const string Runs = "() => window.__runs";

    [Fact]
    public async Task Navigating_to_a_page_with_fewer_top_level_nodes_and_back_runs_the_runtime_once_and_raises_no_error()
    {
        await using var session = await HookSession.OpenAsync<ListRowsPage>(playwright, beforeLoad: CountRunsAsync);
        var page = session.Page;

        await page.ClickAsync("#row-0 [data-testid=edit]");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/edit");
        await page.FillAsync("#field", "ab");
        await Expect(page.Locator("#typed")).ToHaveTextAsync("typed=ab inputs=1");
        await page.ClickAsync("#list");
        await Expect(page.Locator("#row-0")).ToBeVisibleAsync();
        await page.FillAsync("#field", "abc");
        await page.GoBackAsync();
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/edit");
        await page.FillAsync("#field", "abcd");

        await Expect(page.Locator("#typed")).ToHaveTextAsync("typed=abcd inputs=3");
        Assert.Equal(1, await page.EvaluateAsync<int>(Runs));
        Assert.Equal(1, await page.Locator("script[src*='/rask/rask.js']").CountAsync());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Tooltip_buttons_hovered_the_moment_the_page_loads_under_a_slow_cpu_show_and_raise_no_error()
    {
        await using var session = await HookSession.OpenAsync<ListRowsPage>(playwright, beforeLoad: ThrottleAsync);
        var page = session.Page;

        for (var row = 0; row < 4; row++)
        {
            await page.HoverAsync($"#row-{row} [data-testid=edit]");
            await page.HoverAsync($"#row-{row} [data-testid=delete]");
        }

        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('#row-3 [data-testid=delete]').closest('[data-rask-tooltip]').querySelector('[popover]').matches(':popover-open')"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Rows_that_re_render_under_the_pointer_keep_their_tooltips_and_raise_no_error()
    {
        await using var session = await HookSession.OpenAsync<ListRowsPage>(playwright, beforeLoad: CountRunsAsync);
        var page = session.Page;

        await page.HoverAsync("#row-1 [data-testid=delete]");
        await page.ClickAsync("#row-1 [data-testid=delete]");
        await Expect(page.Locator("#row-1")).ToHaveCountAsync(0);
        await page.ClickAsync("#row-2 [data-testid=delete]");
        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=2");
        await page.Mouse.MoveAsync(900, 600);
        await page.HoverAsync("#row-3 [data-testid=edit]");

        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('#row-3 [data-testid=edit]').closest('[data-rask-tooltip]').querySelector('[popover]').matches(':popover-open')"));
        Assert.Equal(1, await page.EvaluateAsync<int>(Runs));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Rows_reached_by_a_navigation_while_the_hooks_are_still_on_their_way_show_the_tooltip_under_the_pointer()
    {
        await using var session = await HookSession.OpenAsync<ListRowsPage>(
            playwright, beforeLoad: async page => { await CountRunsAsync(page); await HoldBundleAsync(page); }, path: "/other");
        var page = session.Page;

        await page.ClickAsync("#list");
        await page.FillAsync("#field", "a");
        await page.HoverAsync("#row-0 [data-testid=edit]");
        await session.HooksLoadedAsync();

        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('#row-0 [data-testid=edit]').closest('[data-rask-tooltip]').querySelector('[popover]').matches(':popover-open')"));
        await Expect(page.Locator("#typed")).ToHaveTextAsync("typed=a inputs=1");
        Assert.Equal(1, await page.EvaluateAsync<int>(Runs));
        Assert.Equal(1, await session.HookBundleRequestsAsync());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task A_second_copy_of_the_runtime_in_one_document_stands_down_and_sends_nothing_of_its_own()
    {
        await using var session = await HookSession.OpenAsync<ListRowsPage>(playwright, beforeLoad: CountRunsAsync);
        var page = session.Page;

        await page.EvaluateAsync("""
            () => new Promise(done => {
                const copy = document.createElement('script');
                copy.src = document.querySelector("script[src*='/rask/rask.js']").src;
                copy.setAttribute('data-rask-managed', '');
                copy.onload = done;
                document.head.append(copy);
            })
            """);
        await page.FillAsync("#field", "ab");
        await page.Keyboard.PressAsync("End");
        await page.ClickAsync("#row-0 [data-testid=delete]");
        await page.HoverAsync("#row-1 [data-testid=edit]");

        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=1");
        await Expect(page.Locator("#typed")).ToHaveTextAsync("typed=ab inputs=1");
        Assert.Equal(2, await page.EvaluateAsync<int>(Runs));
        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('#row-1 [data-testid=edit]').closest('[data-rask-tooltip]').querySelector('[popover]').matches(':popover-open')"));
        Assert.Empty(session.PageErrors);
    }

    // Each run of the runtime asks, once, whether the document is already booted: this counts the asking.
    private static Task CountRunsAsync(IPage page) =>
        page.AddInitScriptAsync("""
            let booted = false;
            window.__runs = 0;
            Object.defineProperty(window, '__raskBooted', {
                get() { window.__runs++; return booted; },
                set(value) { booted = value; },
            });
            """);

    // A machine eight times slower: the page is hovered while it is still starting.
    private static async Task ThrottleAsync(IPage page)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = 8 });
    }

    // The bundle, a slow line's worth late.
    private static Task HoldBundleAsync(IPage page) =>
        page.RouteAsync("**/rask/rask-hooks.js*", async route =>
        {
            await Task.Delay(int.Parse(HoldMs, System.Globalization.CultureInfo.InvariantCulture));
            await route.ContinueAsync();
        });
}

/// <summary>
///     A list whose rows each carry two kit buttons with tooltips, on a page with one top-level node more than
///     the pages it links to.
/// </summary>
public sealed partial class ListRowsPage(RouteState route) : Component
{
    private readonly List<int> _rows = [0, 1, 2, 3, 4, 5];
    private int _deleted;
    private int _inputs;
    private string _typed = string.Empty;

    protected override Component? HeadAssets => Markup.Title["rows"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("path")[$"path={route.Path}"],
        P.Id("deleted")[$"deleted={_deleted}"],
        P.Id("typed")[$"typed={_typed} inputs={_inputs}"],
        NavLink.Href("/").Id("list")["list"],
        NavLink.Href("/other").Id("other")["other"],
        // Only the list has this node, so the runtime's own <script> is one place further on here.
        string.Equals(route.Path, "/", StringComparison.Ordinal) ? Ul[_rows.Select(Row)] : null,
        Input.Value(_typed).Id("field").OnInput(Typed)
    ];

    private Component Row(int row) =>
        Li.Key(row).Id($"row-{row}")[
            Span[$"row {row}"],
            Ui.Button.Ghost.Sm.Square(true).Icon(Ui.IconName.Pencil).Tooltip("Edit")
                .Href(new RouteUrl("/edit", PageType: typeof(ListRowsPage))).Data("testid", "edit"),
            Ui.Button.Ghost.Sm.Square(true).Icon(Ui.IconName.Trash).Tooltip("Delete")
                .OnClick(() => Delete(row)).Data("testid", "delete")
        ];

    private void Delete(int row)
    {
        _rows.Remove(row);
        _deleted++;
    }

    private void Typed(string? value)
    {
        _typed = value ?? string.Empty;
        _inputs++;
    }
}
