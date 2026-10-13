using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A panel that says where it wants to be (<c>data-rask-place</c>) is kept inside the viewport, in a real
///     browser, on a Server page.
/// </summary>
/// <remarks>
///     Measured on Flux UI's live popover, dropdown, context, select and picker pages on 2026-10-13, at
///     1920 × 1080 and 390 × 844, with each trigger held at every edge: the side asked for, then the other
///     alignment, then the opposite side — the first that fits the viewport edge to edge — then the panel slid
///     back to stay 5 px inside the edge it runs along, and a panel taller than what is left held to that and
///     scrolling inside. Every Flux panel was placed in the frame it opened in. The numbers of each family are
///     held to Flux's pages by <c>scripts/flux/placement.mjs</c>; this is the hook itself, in a 1000 × 700 viewport,
///     with panels of 200 × 100 under triggers of 120 × 40.
/// </remarks>
public sealed class RuntimeHookPlaceTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // The panel's box in the viewport, whole pixels.
    private const string Box = "id => { const b = document.getElementById(id).getBoundingClientRect(); return [b.x, b.y, b.width, b.height].map(Math.round).join(','); }";

    // Samples the panel's box after every frame has been drawn, for as long as it is open: a task posted from
    // an animation frame runs once that frame is on screen.
    private const string Watch = """
        id => {
            const panel = document.getElementById(id);
            const drawn = window.__drawn = [];
            const after = new MessageChannel();
            after.port1.onmessage = () => {
                if (panel.matches(':popover-open')) {
                    const b = panel.getBoundingClientRect();
                    drawn.push([b.x, b.y, b.width, b.height].map(Math.round).join(','));
                }
            };
            const frame = () => { after.port2.postMessage(0); requestAnimationFrame(frame); };
            requestAnimationFrame(frame);
        }
        """;

    // Three frames on: a resize is answered in the next frame, and drawn in the one after.
    private const string Frames = "() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(() => requestAnimationFrame(done))))";

    private static async Task<string> OpenedAsync(HookSession session, string id)
    {
        await session.Page.ClickAsync("#" + id + "-open");
        await session.Page.WaitForFunctionAsync("id => document.getElementById(id).hasAttribute('data-rask-placed')", id);
        // Two frames on, whatever was going to move it has.
        await session.Page.EvaluateAsync("() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)))");
        return await session.Page.EvaluateAsync<string>(Box, id);
    }

    [Fact]
    public async Task A_panel_with_room_opens_where_it_asked_to_and_is_the_stylesheets_again_once_closed()
    {
        await using var session = await HookSession.OpenAsync<PlaceHookPage>(playwright);
        await session.HooksLoadedAsync();

        var open = await OpenedAsync(session, "mid");
        await session.Page.Keyboard.PressAsync("Escape");
        await session.Page.WaitForFunctionAsync("() => !document.getElementById('mid').hasAttribute('data-rask-placed')");

        Assert.Equal("400,345,200,100", open);
        Assert.Equal("fixed", await session.Page.EvaluateAsync<string>("() => getComputedStyle(document.getElementById('mid')).position"));
    }

    [Fact]
    public async Task A_panel_that_would_pass_the_right_edge_turns_to_its_triggers_end_and_one_within_five_pixels_is_slid_back()
    {
        await using var session = await HookSession.OpenAsync<PlaceHookPage>(playwright);
        await session.HooksLoadedAsync();

        var turned = await OpenedAsync(session, "right");
        await session.Page.Keyboard.PressAsync("Escape");
        var slid = await OpenedAsync(session, "near");

        Assert.Equal("790,345,200,100", turned);
        Assert.Equal("795,345,200,100", slid);
    }

    [Fact]
    public async Task A_panel_with_no_room_under_its_trigger_opens_over_it()
    {
        await using var session = await HookSession.OpenAsync<PlaceHookPage>(playwright);
        await session.HooksLoadedAsync();

        var open = await OpenedAsync(session, "low");

        Assert.Equal("400,535,200,100", open);
    }

    [Fact]
    public async Task A_panel_taller_than_the_room_is_held_to_what_is_left_on_the_roomier_side_and_stays_scrolled_where_it_was()
    {
        await using var session = await HookSession.OpenAsync<PlaceHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();

        var open = await OpenedAsync(session, "tall");
        await page.EvaluateAsync("() => { document.getElementById('tall').scrollTop = 120; }");
        await page.SetViewportSizeAsync(1000, 600);
        await page.WaitForFunctionAsync("() => Math.round(document.getElementById('tall').getBoundingClientRect().height) === 290");

        // 700 − 345 leaves 355 under the trigger and 295 over it: under it, 5 px from the edge. At 600 tall the
        // room over it is the larger.
        Assert.Equal("100,345,200,350", open);
        Assert.Equal("100,5,200,290", await page.EvaluateAsync<string>(Box, "tall"));
        Assert.Equal("auto", await page.EvaluateAsync<string>("() => getComputedStyle(document.getElementById('tall')).overflowY"));
        Assert.Equal(120, await page.EvaluateAsync<int>("() => document.getElementById('tall').scrollTop"));
    }

    [Fact]
    public async Task A_panel_is_never_drawn_where_the_anchor_rules_alone_would_have_left_it()
    {
        await using var session = await HookSession.OpenAsync<PlaceHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.EvaluateAsync(Watch, "right");

        // A press, which the browser answers by itself; then a script, as a render that opens a panel is.
        var pressed = await OpenedAsync(session, "right");
        var drawnForThePress = await page.EvaluateAsync<string[]>("() => window.__drawn.splice(0)");
        await page.Keyboard.PressAsync("Escape");
        await page.EvaluateAsync("() => document.getElementById('right').showPopover()");
        await page.EvaluateAsync("() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)))");
        var drawnForTheScript = await page.EvaluateAsync<string[]>("() => window.__drawn.splice(0)");

        // The anchor rules alone put it at 870, hanging 70 px off a 1000 px viewport.
        Assert.NotEmpty(drawnForThePress);
        Assert.NotEmpty(drawnForTheScript);
        Assert.All(drawnForThePress.Concat(drawnForTheScript), box => Assert.Equal(pressed, box));
    }

    [Fact]
    public async Task A_menu_at_the_pointer_in_the_bottom_corner_opens_over_the_pointer_and_inside_the_viewport()
    {
        await using var session = await HookSession.OpenAsync<PlaceHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();

        await page.Mouse.ClickAsync(940, 650, new MouseClickOptions { Button = MouseButton.Right });
        await page.WaitForFunctionAsync("() => document.getElementById('ctx').hasAttribute('data-rask-placed')");
        await page.EvaluateAsync("() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)))");

        Assert.Equal("740,545,200,100", await page.EvaluateAsync<string>(Box, "ctx"));
    }

    [Fact]
    public async Task A_render_while_the_panel_is_open_leaves_it_where_it_was_placed()
    {
        await using var session = await HookSession.OpenAsync<PlaceHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();

        var open = await OpenedAsync(session, "live");
        await page.ClickAsync("#bump");
        await page.WaitForFunctionAsync("() => document.getElementById('count').textContent === '1'");

        Assert.Equal("790,145,200,100", open);
        Assert.Equal(open, await page.EvaluateAsync<string>(Box, "live"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.getElementById('live').hasAttribute('data-rask-placed')"));
    }

    [Fact]
    public async Task A_panel_the_stylesheet_takes_under_a_breakpoint_is_left_to_it_and_placed_again_above_it()
    {
        await using var session = await HookSession.OpenAsync<PlaceHookPage>(playwright);
        await session.HooksLoadedAsync();
        var page = session.Page;

        var wide = await OpenedAsync(session, "sheet");
        await page.SetViewportSizeAsync(500, 700);
        await page.EvaluateAsync(Frames);
        var narrow = await page.EvaluateAsync<string>(Box, "sheet");
        await page.SetViewportSizeAsync(1000, 700);
        await page.EvaluateAsync(Frames);
        var back = await page.EvaluateAsync<string>(Box, "sheet");

        Assert.Equal("250,145,200,100", wide);
        Assert.Equal("150,300,200,100", narrow);
        Assert.Equal(wide, back);
    }

    [Fact]
    public async Task Opening_and_placing_a_panel_sends_the_server_nothing()
    {
        var sent = 0;
        await using var session = await HookSession.OpenAsync<PlaceHookPage>(playwright, beforeLoad: page =>
        {
            page.WebSocket += (_, socket) => socket.FrameSent += (_, _) => Interlocked.Increment(ref sent);
            return Task.CompletedTask;
        });
        await session.HooksLoadedAsync();
        var before = Volatile.Read(ref sent);

        await OpenedAsync(session, "right");
        await session.Page.SetViewportSizeAsync(900, 600);
        await session.Page.WaitForTimeoutAsync(300);

        Assert.Equal(before, Volatile.Read(ref sent));
    }
}

/// <summary>Triggers at fixed places in a 1000 × 700 viewport, each with a 200 px panel anchored under its start.</summary>
public sealed partial class PlaceHookPage : Component
{
    private const string Panels = """
        <style>
          body { margin: 0; font: 14px/20px sans-serif }
          .trigger, #area { position: fixed; width: 120px; height: 40px; padding: 0 }
          [popover] { box-sizing: border-box; width: 200px; height: 100px; margin: 0; padding: 0; border: 1px solid #888 }
          #tall { height: auto }
          @media (width >= 640px) { #sheet { inset: auto; top: calc(anchor(bottom) + 5px); left: anchor(left) } }
          @media (width < 640px) { #sheet { --rask-place: none; inset: 0; margin: auto } }
        </style>
        <button id="sheet-open" class="trigger" style="left:250px;top:100px;anchor-name:--sheet" popovertarget="sheet">sheet</button>
        <div id="sheet" popover data-rask-place="bottom start 5 0" style="position-anchor:--sheet">sheet</div>
        <button id="mid-open" class="trigger" style="left:400px;top:300px;anchor-name:--mid" popovertarget="mid">mid</button>
        <div id="mid" popover data-rask-place="bottom start 5 0" style="position-anchor:--mid;inset:auto;top:calc(anchor(bottom) + 5px);left:anchor(left)">mid</div>
        <button id="right-open" class="trigger" style="left:870px;top:300px;anchor-name:--right" popovertarget="right">right</button>
        <div id="right" popover data-rask-place="bottom start 5 0" style="position-anchor:--right;inset:auto;top:calc(anchor(bottom) + 5px);left:anchor(left)">right</div>
        <button id="near-open" class="trigger" style="left:798px;top:300px;anchor-name:--near" popovertarget="near">near</button>
        <div id="near" popover data-rask-place="bottom start 5 0" style="position-anchor:--near;inset:auto;top:calc(anchor(bottom) + 5px);left:anchor(left)">near</div>
        <button id="low-open" class="trigger" style="left:400px;top:640px;anchor-name:--low" popovertarget="low">low</button>
        <div id="low" popover data-rask-place="bottom start 5 0" style="position-anchor:--low;inset:auto;top:calc(anchor(bottom) + 5px);left:anchor(left)">low</div>
        <button id="tall-open" class="trigger" style="left:100px;top:300px;anchor-name:--tall" popovertarget="tall">tall</button>
        <div id="tall" popover data-rask-place="bottom start 5 0" style="position-anchor:--tall;inset:auto;top:calc(anchor(bottom) + 5px);left:anchor(left)"><div style="height:1000px"></div></div>
        <div id="area" style="left:900px;top:620px;width:80px;height:60px" data-rask-contextmenu="ctx">area</div>
        <div id="ctx" popover data-rask-place="bottom end 5 0" style="position:fixed;inset:auto;left:var(--rask-context-x,0px);top:var(--rask-context-y,0px);translate:-100% 5px">menu</div>
        <div style="height:3000px"></div>
        """;

    private int _count;

    protected override Component? HeadAssets => Markup.Title["place hook"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Div[
            Raw.Value(Panels),
            Button.Id("live-open").Class("trigger").Attributes(("popovertarget", "live"), ("style", "left:870px;top:100px;anchor-name:--live"))["live"],
            Div.Id("live").Popover(Popover.Auto).Data("rask-place", "bottom start 5 0")
                .Style("position-anchor:--live;inset:auto;top:calc(anchor(bottom) + 5px);left:anchor(left)")[
                Button.Id("bump").OnClick(() => _count++)["bump"],
                Span.Id("count")[_count.ToString(System.Globalization.CultureInfo.InvariantCulture)]
            ]
        ];
}
