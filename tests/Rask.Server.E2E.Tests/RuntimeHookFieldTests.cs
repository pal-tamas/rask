using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The hooks a form field needs — copy, clear, masks, the switch's Enter, a range's big step, a closed
///     listbox button's keys and the one-time-code cells — in a real browser, with the page's own bound state.
/// </summary>
/// <remarks>
///     Measured on Flux UI's live pages on 2026-10-07. Its copyable input carries <c>data-copyable-copied</c> from
///     the click until 2012 ms after it; its clear button empties the field, hides itself and leaves focus in
///     the field; its switch toggles on Enter and on Space; its slider moves by <c>big-step</c> on Shift+Arrow
///     and PageUp/PageDown and fires <c>input</c> then <c>change</c>; its closed select ignores Enter and opens
///     on the vertical arrows without scrolling the page; and its otp input behaves as the cell tests below say,
///     key for key, including six keys with no delay between them arriving as six characters.
///     <para>
///         The <c>&lt;n&gt;</c> after a cell in the strings below is how many of its characters are selected: 1 on
///         the cell that has focus, so the next key replaces it. Six fast keys once ended <c>6&lt;0&gt;</c> on a slow
///         runner: a render writes the bound field twice, the second record was read after the hook had
///         answered the first, the echoes still waiting were forgotten, and the ones that then arrived emptied
///         the later cells and filled them again — the same six characters, the selection gone. The two
///         late-echo tests force that order, and the one where the echo and the next key share a task.
///     </para>
/// </remarks>
public sealed class RuntimeHookFieldTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Cells = "() => [...document.querySelectorAll('#otp input:not([type=hidden])')].map(i => (i.value || '_') + (document.activeElement === i ? '<' + (i.selectionEnd - i.selectionStart) + '>' : '')).join(' ')";

    // The test is the page here. It answers "1" late, the way a render does — the attribute, then the property —
    // and then answers "12". `sameTask` puts the first answer in the task that types "3", before the runtime's
    // observer has been told of it.
    private const string LateEcho = """
        sameTask => {
            const group = document.getElementById('otp-late');
            const bound = group.querySelector('input[type=hidden]');
            const cells = [...group.querySelectorAll('[aria-label]')];
            const answer = code => { bound.setAttribute('value', code); bound.value = code; };
            const later = () => new Promise(done => setTimeout(done, 0));
            cells.forEach(c => c.value = '');
            cells[0].focus();
            document.execCommand('insertText', false, '1');
            document.execCommand('insertText', false, '2');
            if (sameTask) answer('1');
            document.execCommand('insertText', false, '3');
            return later()
                .then(() => { if (!sameTask) answer('1'); })
                .then(later)
                .then(() => answer('12'))
                .then(later)
                .then(() => cells.map(c => c.value).join('') + '|' + bound.value);
        }
        """;

    [Fact]
    public async Task A_copy_button_writes_the_clipboard_in_the_click_and_says_so_for_two_seconds()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright, new()
        {
            Permissions = ["clipboard-read", "clipboard-write"],
        });
        var page = session.Page;

        await page.ClickAsync("#copy");
        await Expect(page.Locator("#copy[data-copied]")).ToHaveCountAsync(1, new() { Timeout = 500 });
        var clipboard = await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        await page.WaitForTimeoutAsync(1_500);
        var stillSaysSo = await page.Locator("#copy[data-copied]").CountAsync();

        Assert.Equal("FLUX-1234", clipboard);
        Assert.Equal(1, stillSaysSo);
        await Expect(page.Locator("#copy[data-copied]")).ToHaveCountAsync(0, new() { Timeout = 1_000 });
    }

    [Fact]
    public async Task A_clear_button_empties_the_bound_field_tells_the_page_and_leaves_focus_in_it()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        await page.FillAsync("#name", "hello");
        await Expect(page.Locator("#echo")).ToHaveTextAsync("name=hello");
        await page.ClickAsync("#clear");

        await Expect(page.Locator("#echo")).ToHaveTextAsync("name=");
        Assert.Equal(string.Empty, await page.InputValueAsync("#name"));
        Assert.Equal("name", await session.FocusAsync());
    }

    [Fact]
    public async Task A_masked_field_shapes_what_is_typed_and_the_page_receives_the_shaped_value()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        await page.Locator("#phone").PressSequentiallyAsync("7a16x1234567890");
        var typed = await page.InputValueAsync("#phone");
        await page.Locator("#phone").EvaluateAsync("i => i.setSelectionRange(3, 3)");
        await page.Keyboard.TypeAsync("5");
        var inserted = await page.InputValueAsync("#phone");
        var caret = await page.Locator("#phone").EvaluateAsync<int>("i => i.selectionStart");
        await page.Keyboard.PressAsync("Backspace");

        Assert.Equal("(716) 123-4567", typed);
        Assert.Equal("(715) 612-3456", inserted);
        Assert.Equal(4, caret);
        // Flux leaves a deleted character deleted rather than pulling the next one under the caret.
        Assert.Equal("(71) 612-3456", await page.InputValueAsync("#phone"));
        await Expect(page.Locator("#phone-echo")).ToHaveTextAsync("phone=(71) 612-3456");
    }

    [Fact]
    public async Task An_amount_is_grouped_while_it_is_typed()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        await page.Locator("#amount").PressSequentiallyAsync("1234567.891");

        Assert.Equal("1,234,567.89", await page.InputValueAsync("#amount"));
    }

    [Fact]
    public async Task Enter_toggles_a_switch_and_leaves_a_plain_checkbox_alone()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        await page.FocusAsync("#switch");
        await page.Keyboard.PressAsync("Enter");
        var on = await page.IsCheckedAsync("#switch");
        await page.Keyboard.PressAsync("Enter");
        await page.FocusAsync("#plain");
        await page.Keyboard.PressAsync("Enter");

        Assert.True(on);
        Assert.False(await page.IsCheckedAsync("#switch"));
        Assert.False(await page.IsCheckedAsync("#plain"));
    }

    [Fact]
    public async Task Shift_and_an_arrow_move_a_range_by_its_big_step_and_say_so_with_input_then_change()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync("() => { window.__events = []; for (const n of ['input', 'change']) document.getElementById('range').addEventListener(n, () => window.__events.push(n)); }");

        await page.FocusAsync("#range");
        await page.Keyboard.PressAsync("ArrowRight");
        var step = await page.InputValueAsync("#range");
        await page.EvaluateAsync("() => { window.__events = []; }");
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        var big = await page.InputValueAsync("#range");
        var events = await page.EvaluateAsync<string[]>("() => window.__events");
        await page.Keyboard.PressAsync("PageDown");
        var paged = await page.InputValueAsync("#range");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Shift+ArrowUp");

        // Flux's slider (min 0, max 1000, step 1, big-step 100) from 500: 501, 601, 501, and 1000 stays 1000.
        Assert.Equal("501", step);
        Assert.Equal("601", big);
        Assert.Equal(["input", "change"], events);
        Assert.Equal("501", paged);
        Assert.Equal("1000", await page.InputValueAsync("#range"));
    }

    [Fact]
    public async Task Enter_does_not_press_a_closed_listbox_button_and_its_arrows_do_not_scroll_the_page()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        await page.FocusAsync("#listbox-button");
        await page.Keyboard.PressAsync("Enter");
        var afterEnter = await session.ShownAsync("#listbox");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowUp");
        var scrolled = await page.EvaluateAsync<double>("() => scrollY");
        await page.Keyboard.PressAsync("Space");

        Assert.False(afterEnter, "Enter opened the list; Flux's select stays shut on it");
        Assert.Equal(0, scrolled);
        Assert.True(await session.ShownAsync("#listbox"));
    }

    [Fact]
    public async Task A_typed_character_moves_to_the_next_cell_and_the_arrows_stop_at_the_first_empty_one()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#otp [aria-label] >> nth=0");
        await page.Keyboard.TypeAsync("123");
        var typed = await page.EvaluateAsync<string>(Cells);
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.TypeAsync("9");
        var replaced = await page.EvaluateAsync<string>(Cells);
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.TypeAsync("a");

        Assert.Equal("1 2 3 _<0> _ _", typed);
        Assert.Equal("1 9 3<1> _ _ _", replaced);
        Assert.Equal("1 9 3 _<0> _ _", await page.EvaluateAsync<string>(Cells));
        await Expect(page.Locator("#code")).ToHaveTextAsync("code=193");
    }

    [Fact]
    public async Task Backspace_walks_back_and_deleting_closes_the_code_up_to_the_left()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#otp [aria-label] >> nth=0");
        await page.Keyboard.TypeAsync("1234");
        await page.ClickAsync("#otp [aria-label] >> nth=5");
        var pastTheEnd = await page.EvaluateAsync<string>(Cells);
        await page.Keyboard.PressAsync("Backspace");
        var stepped = await page.EvaluateAsync<string>(Cells);
        await page.Keyboard.PressAsync("Backspace");
        var deleted = await page.EvaluateAsync<string>(Cells);
        await page.ClickAsync("#otp [aria-label] >> nth=0");
        await page.Keyboard.PressAsync("Delete");

        Assert.Equal("1 2 3 4 _<0> _", pastTheEnd);
        Assert.Equal("1 2 3 4<1> _ _", stepped);
        Assert.Equal("1 2 3<1> _ _ _", deleted);
        Assert.Equal("2<1> 3 _ _ _ _", await page.EvaluateAsync<string>(Cells));
    }

    [Fact]
    public async Task A_paste_fills_from_the_first_cell_whichever_cell_has_focus()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright, new()
        {
            Permissions = ["clipboard-read", "clipboard-write"],
        });
        var page = session.Page;

        await page.ClickAsync("#otp [aria-label] >> nth=0");
        await page.Keyboard.TypeAsync("123");
        await page.ClickAsync("#otp [aria-label] >> nth=1");
        await page.EvaluateAsync("() => navigator.clipboard.writeText('77')");
        await page.Keyboard.PressAsync("ControlOrMeta+V");
        var two = await page.EvaluateAsync<string>(Cells);
        await page.EvaluateAsync("() => navigator.clipboard.writeText('98 76-5a4321')");
        await page.Keyboard.PressAsync("ControlOrMeta+V");

        Assert.Equal("7 7 _<0> _ _ _", two);
        Assert.Equal("9 8 7 6 5 4<1>", await page.EvaluateAsync<string>(Cells));
        await Expect(page.Locator("#code")).ToHaveTextAsync("code=987654");
    }

    [Fact]
    public async Task Six_keys_with_no_delay_between_them_arrive_as_six_characters_and_survive_the_pages_renders()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#otp [aria-label] >> nth=0");
        await page.Keyboard.TypeAsync("123456", new() { Delay = 0 });
        var typed = await page.EvaluateAsync<string>(Cells);
        await Expect(page.Locator("#code")).ToHaveTextAsync("code=123456");
        // Every late echo of "1", "12", … has been applied by now; none of them may have taken a cell back.
        await page.WaitForTimeoutAsync(300);

        Assert.Equal("1 2 3 4 5 6<1>", typed);
        Assert.Equal("1 2 3 4 5 6<1>", await page.EvaluateAsync<string>(Cells));
    }

    [Fact]
    public async Task Six_characters_inserted_inside_one_task_each_land_in_their_own_cell()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#otp [aria-label] >> nth=0");
        await page.EvaluateAsync("() => { for (const c of '123456') document.execCommand('insertText', false, c); }");

        Assert.Equal("1 2 3 4 5 6<1>", await page.EvaluateAsync<string>(Cells));
        await Expect(page.Locator("#code")).ToHaveTextAsync("code=123456");
    }

    [Fact]
    public async Task A_late_echo_written_twice_by_one_render_does_not_forget_the_echoes_still_to_come()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        var state = await page.EvaluateAsync<string>(LateEcho, false);

        Assert.Equal("123|123", state);
    }

    [Fact]
    public async Task An_echo_that_arrives_in_the_same_task_as_the_next_key_is_still_an_echo()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        var state = await page.EvaluateAsync<string>(LateEcho, true);

        Assert.Equal("123|123", state);
    }

    [Fact]
    public async Task The_cells_follow_the_page_when_the_page_clears_the_code()
    {
        await using var session = await HookSession.OpenAsync<FieldHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#otp [aria-label] >> nth=0");
        await page.Keyboard.TypeAsync("1234");
        await Expect(page.Locator("#code")).ToHaveTextAsync("code=1234");
        await page.ClickAsync("#reset");

        await Expect(page.Locator("#code")).ToHaveTextAsync("code=");
        await Expect(page.Locator("#otp [aria-label] >> nth=0")).ToHaveValueAsync(string.Empty);
        Assert.Equal("_ _ _ _ _ _", (await page.EvaluateAsync<string>(Cells)).Replace("<0>", string.Empty, StringComparison.Ordinal));
    }
}

/// <summary>The attributes of the field hooks, around three fields the page binds.</summary>
public sealed partial class FieldHookPage : Component
{
    private const string Html = """
        <style>body { margin: 0; height: 3000px } #otp input { width: 24px }</style>
        <input id="secret" value="FLUX-1234" readonly>
        <button id="copy" type="button" data-rask-copy="secret">Copy</button>
        <input id="amount" data-rask-mask-money>
        <input id="switch" type="checkbox" role="switch">
        <form onsubmit="return false"><input id="plain" type="checkbox"></form>
        <input id="range" type="range" min="0" max="1000" step="1" value="500" data-rask-big-step="100">
        <button id="listbox-button" type="button" role="combobox" aria-haspopup="listbox" aria-expanded="false"
                popovertarget="listbox" data-rask-listbox-button>Choose</button>
        <div id="listbox" popover role="listbox">Options</div>
        <div id="otp-late" data-rask-otp><input type="hidden"><input aria-label="1 of 3"><input aria-label="2 of 3"><input aria-label="3 of 3"></div>
        """;

    private string _name = string.Empty;
    private string _phone = string.Empty;
    private string _code = string.Empty;

    protected override Component? HeadAssets => Markup.Title["field hooks"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("echo")[$"name={_name}"],
        P.Id("phone-echo")[$"phone={_phone}"],
        P.Id("code")[$"code={_code}"],
        Input.Value(_name).Id("name").OnInput(v => _name = v ?? string.Empty),
        Button.Id("clear").Data("rask-clear", "name")["Clear"],
        Input.Value(_phone).Id("phone").Data("rask-mask", "(999) 999-9999").OnInput(v => _phone = v ?? string.Empty),
        Div.Id("otp").Data("rask-otp", "numeric")[
            Input.Value(_code).Type(InputType.Hidden).OnInput(v => _code = v ?? string.Empty),
            Enumerable.Range(1, 6).Select(i => (Component)Input.Of<string>().AriaLabel($"Character {i} of 6").Attributes(("inputmode", "numeric"))).ToArray()
        ],
        Button.Id("reset").OnClick(() => _code = string.Empty)["Reset"],
        Div[Raw.Value(Html)]
    ];
}
