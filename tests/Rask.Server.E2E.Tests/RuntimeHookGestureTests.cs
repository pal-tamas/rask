using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The hooks for what a round trip is too slow or too blind for — a value dragged under the pointer, a
///     control for an API the browser may not have, and a file input that says how far its upload has got —
///     in a real browser.
/// </summary>
/// <remarks>
///     Measured on Flux UI's live colour picker and file upload pages on 2026-10-07: its eyedropper button is
///     <c>hidden</c> where the browser has no <c>EyeDropper</c>, not disabled; its upload carries
///     <c>--flux-file-upload-progress: 0%</c> and <c>--flux-file-upload-progress-as-string: '0%'</c> — whole
///     percents, the second one quoted — beside <c>data-loading</c>. Its docs demo never sends a file, so
///     WHEN the mark comes off is Rask's own: when the handler that received the files has rendered.
/// </remarks>
public sealed class RuntimeHookGestureTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // Only what the field under test says: the measured box on the same page announces its size whenever it likes.
    private const string Record = "id => { window.heard = []; for (const type of ['input', 'change']) document.addEventListener(type, e => { if (e.target.id === id) window.heard.push(type + ':' + e.target.value); }); }";
    private const string Area = "() => { const s = document.getElementById('area').style; return s.getPropertyValue('--rask-drag-x') + ' ' + s.getPropertyValue('--rask-drag-y'); }";

    [Fact]
    public async Task A_press_on_a_drag_surface_writes_where_the_pointer_is_at_once_and_tells_the_page_through_its_field()
    {
        await using var session = await HookSession.OpenAsync<GestureHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync(Record, "area-value");

        // The surface is 220 x 120 at (100, 100) with an inset of 10: its track is 200 x 100 from (110, 110).
        await page.Mouse.MoveAsync(160, 135);
        await page.Mouse.DownAsync();
        var pressed = await page.EvaluateAsync<string>(Area);
        await page.WaitForFunctionAsync("() => window.heard.length === 1");
        var whileHeld = await page.EvaluateAsync<string>("() => window.heard.join(' | ')");
        await page.Mouse.UpAsync();

        Assert.Equal("0.25 0.25", pressed);
        Assert.Equal("input:0.25 0.25", whileHeld);
        Assert.Equal("input:0.25 0.25 | change:0.25 0.25", await page.EvaluateAsync<string>("() => window.heard.join(' | ')"));
    }

    [Fact]
    public async Task A_drag_follows_the_pointer_outside_the_surface_stops_at_its_ends_and_settles_once_on_release()
    {
        await using var session = await HookSession.OpenAsync<GestureHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync(Record, "area-value");

        await page.Mouse.MoveAsync(160, 135);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(260, 160, new() { Steps = 5 });
        var inside = await page.EvaluateAsync<string>(Area);
        await page.Mouse.MoveAsync(900, 20, new() { Steps = 5 });
        var outside = await page.EvaluateAsync<string>(Area);
        await page.Mouse.UpAsync();
        await page.Mouse.MoveAsync(110, 110);

        Assert.Equal("0.75 0.5", inside);
        Assert.Equal("1 0", outside);
        Assert.Equal("1 0", await page.EvaluateAsync<string>(Area));
        Assert.Equal("1 0", await page.InputValueAsync("#area-value"));
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.heard.filter(h => h.startsWith('change')).length"));
        Assert.Equal("change:1 0", await page.EvaluateAsync<string>("() => window.heard[window.heard.length - 1]"));
    }

    [Fact]
    public async Task A_surface_with_one_axis_carries_one_number_and_follows_the_value_the_page_writes()
    {
        await using var session = await HookSession.OpenAsync<GestureHookPage>(playwright);
        var page = session.Page;
        const string track = "() => { const s = document.getElementById('track').style; return s.getPropertyValue('--rask-drag-x') + '|' + s.getPropertyValue('--rask-drag-y'); }";

        var rendered = await page.EvaluateAsync<string>(track);
        await page.Mouse.ClickAsync(150, 310);
        var clicked = await page.InputValueAsync("#track-value");
        await page.EvaluateAsync("() => document.getElementById('track-value').setAttribute('value', '0.9')");
        await page.WaitForFunctionAsync("() => document.getElementById('track').style.getPropertyValue('--rask-drag-x') === '0.9'");

        // Rendered with value="0.3"; no inset, 200 wide from 100.
        Assert.Equal("0.3|", rendered);
        Assert.Equal("0.25", clicked);
    }

    [Fact]
    public async Task The_row_nearest_the_pointer_is_lit_without_a_round_trip_and_the_tooltip_keeps_beside_it_inside_the_plot()
    {
        await using var session = await HookSession.OpenAsync<GestureHookPage>(playwright);
        var page = session.Page;
        const string read = "() => { const p = document.getElementById('plot'); return [p.hasAttribute('data-active') ? 'on' : 'off', [...p.querySelectorAll('[data-rask-plot-row][data-active]')].map(r => r.getAttribute('data-rask-plot-row')).join(','), p.style.getPropertyValue('--rask-plot-x'), p.style.getPropertyValue('--rask-pointer-x'), p.style.getPropertyValue('--rask-pointer-y'), document.getElementById('plot-tip').style.transform].join(' | '); }";

        // The plot is 400 x 200 at (500, 100); its area 300 x 150 at (50, 20) inside it; rows at 0, 0.5 and 1.
        await page.Mouse.MoveAsync(610, 170);
        var first = await page.EvaluateAsync<string>(read);
        await page.Mouse.MoveAsync(628, 170);
        var pastTheMidpoint = await page.EvaluateAsync<string>(read);
        await page.Mouse.MoveAsync(835, 260);
        var last = await page.EvaluateAsync<string>(read);
        await page.Mouse.MoveAsync(520, 110);

        Assert.Equal("on | 0,0 | 50px | 110px | 70px | translate(60px, 80px)", first);
        Assert.Equal("on | 1,1 | 200px | 128px | 70px | translate(210px, 80px)", pastTheMidpoint);
        // At the right and bottom edges the 80 x 40 tooltip goes to the other side of the row and of the pointer.
        Assert.Equal("on | 2,2 | 350px | 335px | 160px | translate(260px, 110px)", last);
        Assert.StartsWith("off |  | 350px", await page.EvaluateAsync<string>(read), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_measured_element_keeps_its_field_at_its_own_content_box_and_tells_the_page_when_it_changes()
    {
        await using var session = await HookSession.OpenAsync<GestureHookPage>(playwright);
        var page = session.Page;
        await page.WaitForFunctionAsync("() => document.getElementById('box-size').value === '300 100'");
        await page.EvaluateAsync(Record, "box-size");

        await page.EvaluateAsync("() => { document.getElementById('box').style.width = '250.5px'; }");
        await page.WaitForFunctionAsync("() => document.getElementById('box-size').value === '250.5 100'");

        Assert.Equal("input:250.5 100 | change:250.5 100", await page.EvaluateAsync<string>("() => window.heard.join(' | ')"));
    }

    [Fact]
    public async Task A_measured_element_whose_field_the_page_writes_over_tells_the_page_its_box_again()
    {
        await using var session = await HookSession.OpenAsync<GestureHookPage>(playwright);
        var page = session.Page;
        await page.WaitForFunctionAsync("() => document.getElementById('box-size').value === '300 100'");
        await page.EvaluateAsync(Record, "box-size");

        // What a host taking over a prerendered page does: it writes the size it rendered, which is not the box.
        await page.EvaluateAsync("() => { document.getElementById('box-size').setAttribute('value', '600 200'); }");
        await page.WaitForFunctionAsync("() => document.getElementById('box-size').value === '300 100'");
        await page.EvaluateAsync("() => { document.getElementById('box-size').setAttribute('value', '300.2 100'); }");
        await page.WaitForTimeoutAsync(100);

        // Told once: a size within half a pixel of the box is the box, and nothing is said about it.
        Assert.Equal("input:300 100 | change:300 100", await page.EvaluateAsync<string>("() => window.heard.join(' | ')"));
    }

    [Fact]
    public async Task A_control_is_hidden_where_the_global_it_requires_is_missing_and_shown_where_it_is_there()
    {
        await using var session = await HookSession.OpenAsync<GestureHookPage>(playwright);
        var page = session.Page;
        const string hidden = "() => ['has', 'lacks', 'unsafe'].map(id => document.getElementById(id).hidden).join(' ')";

        var atArrival = await page.EvaluateAsync<string>(hidden);
        await page.EvaluateAsync("() => document.body.insertAdjacentHTML('beforeend', '<button id=later data-rask-requires=NoSuchGlobal>later</button>')");
        await page.WaitForFunctionAsync("() => document.getElementById('later').hidden");

        // `has` was rendered hidden and names `document`; `unsafe` names an expression, which is never evaluated.
        Assert.Equal("False True True", atArrival, ignoreCase: true);
        Assert.False(await page.EvaluateAsync<bool>("() => window.evaluated === true"));
    }

    [Fact]
    public async Task A_file_input_marks_the_element_around_it_and_says_how_far_the_upload_has_got_until_its_handler_has_rendered()
    {
        await using var session = await HookSession.OpenAsync<GestureHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync("""
            () => {
                window.seen = [];
                const zone = document.getElementById('zone');
                const note = () => window.seen.push((zone.hasAttribute('data-loading') ? 'loading' : 'idle') + ' ' + zone.style.getPropertyValue('--rask-progress') + ' ' + zone.style.getPropertyValue('--rask-progress-as-string'));
                new MutationObserver(note).observe(zone, { attributes: true });
            }
            """);

        await page.SetInputFilesAsync("#file", new Microsoft.Playwright.FilePayload { Name = "big.bin", MimeType = "application/octet-stream", Buffer = new byte[3_000_000] });
        await Expect(page.Locator("#received")).ToHaveTextAsync("received=3000000");
        await page.WaitForFunctionAsync("() => !document.getElementById('zone').hasAttribute('data-loading')");
        var seen = await page.EvaluateAsync<string[]>("() => window.seen");

        Assert.Contains("loading 0% '0%'", seen);
        Assert.Contains("loading 100% '100%'", seen);
        Assert.All(seen.Where(s => s.StartsWith("loading", StringComparison.Ordinal)), s => Assert.Matches("^loading [0-9]+% '[0-9]+%'$", s));
        Assert.Equal(string.Empty, await page.EvaluateAsync<string>("() => document.getElementById('zone').style.getPropertyValue('--rask-progress')"));
    }
}

/// <summary>Two drag surfaces, a plot, a measured box, three gated buttons and a dropzone around a file input.</summary>
public sealed partial class GestureHookPage : Component
{
    private const string Html = """
        <div id="area" data-rask-drag="x y" data-rask-drag-inset="10"
             style="position:absolute;left:100px;top:100px;width:220px;height:120px;background:#ccc;touch-action:none">
            <input id="area-value" type="hidden">
        </div>
        <div id="track" data-rask-drag="x"
             style="position:absolute;left:100px;top:300px;width:200px;height:20px;background:#ccc;touch-action:none">
            <input id="track-value" type="hidden" value="0.3">
        </div>
        <div id="plot" data-rask-plot="0 0.5 1" style="position:absolute;left:500px;top:100px;width:400px;height:200px;background:#eee">
            <div data-rask-plot-area style="position:absolute;left:50px;top:20px;width:300px;height:150px;background:#ddd"></div>
            <i data-rask-plot-row="0"></i><i data-rask-plot-row="1"></i><i data-rask-plot-row="2"></i>
            <b data-rask-plot-row="0"></b><b data-rask-plot-row="1"></b><b data-rask-plot-row="2"></b>
            <div id="plot-tip" data-rask-plot-tooltip="10" style="position:absolute;left:0;top:0;width:80px;height:40px"></div>
        </div>
        <div id="box" data-rask-measure style="position:absolute;left:500px;top:400px;width:300px;height:100px;padding:10px">
            <input id="box-size" type="hidden">
        </div>
        <button id="has" type="button" hidden data-rask-requires="document">has</button>
        <button id="lacks" type="button" data-rask-requires="NoSuchGlobal">lacks</button>
        <button id="unsafe" type="button" data-rask-requires="(window.evaluated=true)">unsafe</button>
        """;

    private long _received;

    protected override Component? HeadAssets => Markup.Title["gesture hooks"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("received")[$"received={_received}"],
        Div.Id("zone").Data("rask-loading", string.Empty).Style("position:absolute;left:100px;top:400px")[
            Input.Value<string>(null).Id("file").Type(InputType.File).OnFiles(files => _received = files.Sum(f => f.Size))
        ],
        Div[Raw.Value(Html)]
    ];
}
