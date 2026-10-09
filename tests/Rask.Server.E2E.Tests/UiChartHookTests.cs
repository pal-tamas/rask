using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Chart</c> on a live SERVER page: the drawing follows its container's size over the socket, and
///     what follows the pointer never touches the socket at all.
/// </summary>
/// <remarks>
///     <c>RuntimeHookGestureTests</c> pins the plot and measuring hooks against attributes written by hand;
///     <c>scripts/flux/parity-chart.mjs --pointer</c> holds the kit's chart to Flux's under a pointer on a static
///     page. This is the seam between them: a render that says <c>data-rask-measure</c>, the size coming back as
///     an <c>input</c> a round trip later, and the chart drawn again for it — as Flux's is, measured on its live
///     page on 2026-10-09 (drawn for its box, again in the frame its box changes, never while it has none).
/// </remarks>
public sealed class UiChartHookTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string ViewBox = "() => document.querySelector('#fluid svg').getAttribute('viewBox')";

    [Fact]
    public async Task A_chart_is_drawn_for_the_box_the_browser_measured_and_again_when_that_box_changes()
    {
        await using var session = await HookSession.OpenAsync<ChartHookPage>(playwright);
        var page = session.Page;

        await page.WaitForFunctionAsync("() => document.querySelector('#fluid svg').getAttribute('viewBox') === '0 0 400 133.33'");
        var widthAtFirst = await page.EvaluateAsync<double>("() => document.querySelector('#fluid svg').getBoundingClientRect().width");
        await page.EvaluateAsync("() => { document.getElementById('fluid').style.width = '300px'; }");
        await page.WaitForFunctionAsync("() => document.querySelector('#fluid svg').getAttribute('viewBox') === '0 0 300 100'");
        var area = await page.EvaluateAsync<string>("() => { const a = document.querySelector('#fluid [data-rask-plot-area]').getBoundingClientRect(), s = document.querySelector('#fluid svg').getBoundingClientRect(); return [a.left - s.left, a.top - s.top, a.right - s.left, a.bottom - s.top].map(Math.round).join(' '); }");

        // A drawing in the box's own units, so nothing in it is scaled; and the plot area is the plot's, gutters in.
        Assert.Equal(400, widthAtFirst);
        Assert.Equal("8 8 292 92", area);
    }

    [Fact]
    public async Task A_hidden_chart_keeps_its_drawing_and_is_drawn_for_its_new_box_when_it_is_shown_again()
    {
        await using var session = await HookSession.OpenAsync<ChartHookPage>(playwright);
        var page = session.Page;
        await page.WaitForFunctionAsync("() => document.querySelector('#fluid svg').getAttribute('viewBox') === '0 0 400 133.33'");

        await page.EvaluateAsync("() => { const f = document.getElementById('fluid'); f.style.display = 'none'; f.style.width = '240px'; }");
        await page.WaitForTimeoutAsync(400);
        var whileHidden = await page.EvaluateAsync<string>(ViewBox);
        await page.EvaluateAsync("() => { document.getElementById('fluid').style.display = ''; }");
        await page.WaitForFunctionAsync("() => document.querySelector('#fluid svg').getAttribute('viewBox') === '0 0 240 80'");

        Assert.Equal("0 0 400 133.33", whileHidden);
    }

    [Fact]
    public async Task A_chart_stated_at_the_size_it_turns_out_to_have_tells_the_page_nothing()
    {
        var heard = new List<string>();
        await using var session = await HookSession.OpenAsync<ChartHookPage>(playwright, beforeLoad: page =>
            page.AddInitScriptAsync("window.sized = []; document.addEventListener('input', e => window.sized.push(e.target.closest('[id]').id + ':' + e.target.value), true);"));
        var page = session.Page;

        await page.WaitForFunctionAsync("() => document.querySelector('#fluid svg').getAttribute('viewBox') === '0 0 400 133.33'");
        heard.AddRange(await page.EvaluateAsync<string[]>("() => window.sized"));

        // The fluid chart was drawn for 600 by 200 and measured at 400 wide; the stated one was already right.
        Assert.Equal(["fluid:400 133.33"], heard);
        Assert.Equal("0 0 200 100", await page.EvaluateAsync<string>("() => document.querySelector('#stated svg').getAttribute('viewBox')"));
    }

    [Fact]
    public async Task The_tooltip_the_cursor_and_the_summary_follow_the_pointer_with_nothing_sent_to_the_server()
    {
        var sent = 0;
        await using var session = await HookSession.OpenAsync<ChartHookPage>(playwright, beforeLoad: page =>
        {
            page.WebSocket += (_, socket) => socket.FrameSent += (_, _) => Interlocked.Increment(ref sent);
            return Task.CompletedTask;
        });
        var page = session.Page;
        await page.WaitForFunctionAsync("() => document.querySelector('#fluid svg').getAttribute('viewBox') === '0 0 400 133.33'");
        await session.HooksLoadedAsync();
        const string read = "() => { const c = document.querySelector('#fluid [data-ui-chart]'), t = c.querySelector('[data-rask-plot-tooltip]'), f = c.getBoundingClientRect(), b = t.getBoundingClientRect(); return [c.hasAttribute('data-active') ? 'on' : 'off', t.hasAttribute('data-active') ? t.innerText.replace(/\\s+/g, ' ').trim() : '-', Math.round(b.left - f.left) + ',' + Math.round(b.top - f.top), c.querySelector('slot').textContent, c.style.getPropertyValue('--rask-plot-at'), [...c.querySelectorAll('circle')].map(p => p.hasAttribute('data-active') ? 'A' : '.').join('')].join(' | '); }";
        var before = Volatile.Read(ref sent);

        // The chart is at (20, 20), 400 by 133.33; its plot runs from 8 to 392 across and its three rows sit at 8, 200 and 392.
        await page.Mouse.MoveAsync(40, 60);
        var first = await page.EvaluateAsync<string>(read);
        await page.Mouse.MoveAsync(230, 70);
        var middle = await page.EvaluateAsync<string>(read);
        await page.Mouse.MoveAsync(230, 160);
        var below = await page.EvaluateAsync<string>(read);

        Assert.Equal("on | Mon 10 | 23,55 | 10 | 0 | A..", first);
        Assert.Equal("on | Tue 30 | 215,65 | 30 | 0.5 | .A.", middle);
        Assert.StartsWith("off | - | 215,65 | 20 | ", below, StringComparison.Ordinal);
        Assert.Equal(before, Volatile.Read(ref sent));
    }

    [Fact]
    public async Task A_pies_slice_under_the_pointer_is_active_the_others_inactive_and_the_tooltip_is_its_colour()
    {
        await using var session = await HookSession.OpenAsync<ChartHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        const string read = "() => { const c = document.querySelector('#pie [data-ui-chart]'), t = c.querySelector('[data-rask-plot-tooltip]'); return [[...c.querySelectorAll('svg path')].map(p => p.hasAttribute('data-active') ? 'A' : p.hasAttribute('data-inactive') ? 'i' : '.').join(''), t.hasAttribute('data-active') ? t.innerText.replace(/\\s+/g, ' ').trim() : '-', [...t.querySelectorAll('[data-rask-plot-row]')].map(d => d.hasAttribute('data-active') ? 'A' : '.').join('')].join(' | '); }";

        // The pie is 200 by 200 at (20, 300): Mon is its first sixth, clockwise from twelve, and Wed its last third.
        await page.Mouse.MoveAsync(150, 350);
        var first = await page.EvaluateAsync<string>(read);
        await page.Mouse.MoveAsync(70, 400);
        var last = await page.EvaluateAsync<string>(read);
        await page.Mouse.MoveAsync(22, 302);

        Assert.Equal("Aii | Mon 10 | A..", first);
        Assert.Equal("iiA | Wed 20 | ..A", last);
        Assert.Equal("... | - | ...", await page.EvaluateAsync<string>(read));
    }
}

/// <summary>A chart that fills its container, one stated at its real size, and a pie.</summary>
public sealed partial class ChartHookPage : Component
{
    private sealed record Day(string Name, int Sold);

    private static readonly Day[] Week = [new("Mon", 10), new("Tue", 30), new("Wed", 20)];

    // The handful of the kit's utilities the chart's own box needs; the page has no stylesheet.
    private const string Layout =
        "body{margin:0}.relative{position:relative}.absolute{position:absolute}.block{display:block}.inset-0{inset:0}"
        + ".size-full{width:100%;height:100%}.hidden{display:none}.wide{aspect-ratio:3/1}.square{aspect-ratio:1}"
        + ".pointer-events-none{pointer-events:none}";

    protected override Component? HeadAssets => [Markup.Title["chart hooks"], Markup.Style[Raw.Value(Layout)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Div.Id("fluid").Style("position:absolute;left:20px;top:20px;width:400px")[
            Ui.Chart.Value(Week).Class("wide")[
                Ui.ChartSvg[
                    Ui.ChartLine.Field((Day d) => d.Sold),
                    Ui.ChartPoint.Field((Day d) => d.Sold).R(0),
                    Ui.ChartCursor
                ],
                Ui.ChartSummary[Ui.ChartSummaryValue.Field((Day d) => d.Sold)],
                Ui.ChartTooltip[Ui.ChartTooltipHeading.Field((Day d) => d.Name), Ui.ChartTooltipValue.Field((Day d) => d.Sold)]
            ]
        ],
        Div.Id("stated").Style("position:absolute;left:500px;top:20px;width:200px;height:100px")[
            Ui.Chart.Value(Week).Class("size-full")[Ui.ChartSvg.Width(200).Height(100)[Ui.ChartLine.Field((Day d) => d.Sold)]]
        ],
        Div.Id("pie").Style("position:absolute;left:20px;top:300px;width:200px")[
            Ui.Chart.Value(Week).Class("square")[
                Ui.ChartSvg.Width(200).Height(200)[Ui.ChartPie.Field((Day d) => d.Sold).LabelField((Day d) => d.Name)],
                Ui.ChartTooltip[Ui.ChartTooltipValue.Field((Day d) => d.Sold).LabelField((Day d) => d.Name)[Ui.ChartTooltipIndicator]]
            ]
        ]
    ];
}
