using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The hooks that keep state a render does not have — a group of toasts held by one pointer, the stack's
///     measurements, a checkbox kept across visits and one dropped on navigation — in a real browser.
/// </summary>
/// <remarks>
///     Measured on Flux UI's live toast page on 2026-10-07: a toast shown for 1000 ms and hovered for 3000 ms
///     lived 4359 ms after the pointer left (the remainder of its 5000 ms plus the exit animation — it RESUMES,
///     it does not restart); three toasts in a group hovered for 6.7 s were all still there; and a toast whose
///     close button had focus ran out on time (5366 ms), so focus holds nothing there. Its sidebar keeps
///     <c>localStorage["flux-sidebar-collapsed-desktop"]</c>.
/// </remarks>
public sealed class RuntimeHookStateTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task The_pointer_anywhere_over_a_group_holds_every_toast_in_it_and_they_run_on_when_it_leaves()
    {
        await using var session = await HookSession.OpenAsync<StateHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#show");
        await Expect(page.Locator("[role=status]")).ToHaveCountAsync(2);
        // Over the group's own padding: on neither toast.
        await page.Mouse.MoveAsync(30, 690);
        await page.WaitForTimeoutAsync(2_000);
        var whileHeld = await page.Locator("[role=status]").CountAsync();
        await page.Mouse.MoveAsync(900, 20);

        Assert.Equal(2, whileHeld);
        await Expect(page.Locator("#dismissed")).ToHaveTextAsync("dismissed=2", new() { Timeout = 3_000 });
    }

    [Fact]
    public async Task Focus_inside_a_toast_that_only_the_pointer_holds_does_not_stop_its_countdown()
    {
        await using var session = await HookSession.OpenAsync<StateHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#show");
        await page.Locator("[role=status] button").First.FocusAsync();
        await page.Mouse.MoveAsync(900, 20);

        await Expect(page.Locator("#dismissed")).ToHaveTextAsync("dismissed=2", new() { Timeout = 3_000 });
    }

    [Fact]
    public async Task Each_toast_in_a_stack_knows_its_place_its_height_the_height_in_front_of_it_and_the_front_ones()
    {
        await using var session = await HookSession.OpenAsync<StateHookPage>(playwright);
        var page = session.Page;
        const string read = "() => [...document.querySelectorAll('[role=status]')].map(t => ['index', 'height', 'offset', 'front'].map(n => t.style.getPropertyValue('--rask-stack-' + n)).join('/')).join(' ')";

        await page.ClickAsync("#show");
        await Expect(page.Locator("[role=status]")).ToHaveCountAsync(2);
        var two = await page.EvaluateAsync<string>(read);
        await page.Mouse.MoveAsync(30, 690);
        await page.Locator("[role=status] button").Last.ClickAsync();
        await Expect(page.Locator("[role=status]")).ToHaveCountAsync(1);

        // The last child is the front of the stack: index 0, nothing in front of it.
        Assert.Equal("1/40px/60px/60px 0/60px/0px/60px", two);
        Assert.Equal("0/40px/0px/40px", await page.EvaluateAsync<string>(read));
    }

    [Fact]
    public async Task Cards_cut_to_the_front_ones_height_are_still_measured_at_their_own_and_all_know_the_front_ones()
    {
        await using var session = await HookSession.OpenAsync<StateHookPage>(playwright);
        var page = session.Page;
        const string read = "() => [...document.querySelectorAll('#deck > div')].map(t => ['height', 'offset', 'front'].map(n => t.style.getPropertyValue('--rask-stack-' + n)).join('/') + '=' + t.offsetHeight).join(' ')";

        var atRest = await page.EvaluateAsync<string>(read);
        await page.EvaluateAsync("() => document.getElementById('deck').insertAdjacentHTML('beforeend', '<div><div><div style=\"height:20px\"></div></div></div>')");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('#deck > div')[2].style.getPropertyValue('--rask-stack-front') === '30px'");

        // Tall (80 with its padding) behind short (50): each says its own height, and the card inside both is
        // cut to the front one's, 50, so both are drawn 60 tall.
        Assert.Equal("80px/50px/50px=60 50px/0px/50px=60", atRest);
        Assert.Equal("80px/80px/30px 50px/30px/30px 30px/0px/30px", System.Text.RegularExpressions.Regex.Replace(await page.EvaluateAsync<string>(read), "=[0-9.]+", string.Empty));
        Assert.False(await page.EvaluateAsync<bool>("() => document.getElementById('deck').hasAttribute('data-rask-measuring')"));
    }

    [Fact]
    public async Task A_persisted_checkbox_is_stored_when_it_changes_and_comes_back_as_it_was_left()
    {
        await using var session = await HookSession.OpenAsync<StateHookPage>(playwright);
        var page = session.Page;

        await page.CheckAsync("#collapsed");
        var stored = await page.EvaluateAsync<string?>("() => localStorage.getItem('test-sidebar-collapsed')");
        await page.ReloadAsync();
        var restored = await page.IsCheckedAsync("#collapsed");
        // A render of the page around it: the server never rendered `checked`, and must not take it back.
        await page.ClickAsync("#show");
        await Expect(page.Locator("[role=status]")).ToHaveCountAsync(2);

        Assert.Equal("true", stored);
        Assert.True(restored, "the box came back unchecked: its stored state was not restored");
        Assert.True(await page.IsCheckedAsync("#collapsed"));
    }

    [Fact]
    public async Task A_drawer_checkbox_is_unchecked_when_the_app_navigates_to_another_path_and_not_for_a_query()
    {
        await using var session = await HookSession.OpenAsync<StateHookPage>(playwright);
        var page = session.Page;

        await page.CheckAsync("#drawer");
        await page.EvaluateAsync("() => history.replaceState(null, '', '?tab=2')");
        var afterQuery = await page.IsCheckedAsync("#drawer");
        await page.EvaluateAsync("() => history.pushState(null, '', '/elsewhere')");

        Assert.True(afterQuery);
        Assert.False(await page.IsCheckedAsync("#drawer"));
    }
}

/// <summary>A group of two toasts the page owns, and two checkboxes it does not.</summary>
public sealed partial class StateHookPage : Component
{
    private const string Html = """
        <input id="collapsed" type="checkbox" data-rask-persist="test-sidebar-collapsed">
        <input id="drawer" type="checkbox" data-rask-uncheck-on-navigate>
        <style>
            #deck > div { padding: 5px 0; }
            #deck > div > div { overflow: hidden; transition: height 0.35s ease; }
            #deck:not([data-rask-measuring]) > div > div { height: var(--rask-stack-front, auto); }
        </style>
        <div id="deck" data-rask-stack style="position:absolute;top:0;right:0;width:100px">
            <div><div><div style="height:70px"></div></div></div>
            <div><div><div style="height:40px"></div></div></div>
        </div>
        """;

    private readonly List<int> _toasts = [];
    private int _dismissed;

    protected override Component? HeadAssets => Markup.Title["state hooks"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("dismissed")[$"dismissed={_dismissed}"],
        Button.Id("show").OnClick(() => _toasts.AddRange([40, 60]))["show"],
        Div[Raw.Value(Html)],
        Div.Id("group")
            .Attributes(("data-rask-dismiss-scope", null), ("data-rask-stack", null))
            .Style("position:fixed;left:0;bottom:0;width:300px;padding:20px")[
            _toasts.Select(height => (Component)Div.Key(height.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Role("status")
                // Not the same instant: two presses in one frame would race each other's handler ids.
                .Attributes(("data-rask-dismiss-after", height == 40 ? "1000" : "1300"), ("data-rask-dismiss-hold", "pointer"))
                .Style($"height:{height}px;box-sizing:border-box")[
                Button.Attributes(("data-rask-dismiss", null)).OnClick(() => { _toasts.Remove(height); _dismissed++; })["Dismiss"]
            ]).ToArray()
        ]
    ];
}
