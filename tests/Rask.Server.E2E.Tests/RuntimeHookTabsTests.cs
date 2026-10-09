using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The hook for a <c>role="tablist"</c>'s arrow keys, in a real browser, on a page a session drives.
/// </summary>
/// <remarks>
///     Recorded on Flux UI's live tabs page on 2026-10-08, every example: all four arrows move focus AND
///     select, around the ends, and never scroll the page; they pass over the "Add tab" action, which Tab
///     reaches; Home and End are the page's. Nothing on the page asks for a hook but the role, so this is also
///     the proof that a tablist alone loads the bundle.
/// </remarks>
public sealed class RuntimeHookTabsTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_page_whose_only_hooked_element_is_a_tablist_gets_the_bundle_in_its_first_response()
    {
        await using var session = await HookSession.OpenAsync<TabsHookPage>(playwright);
        var page = session.Page;

        var firstResponse = await (await page.APIRequest.GetAsync(session.BaseUrl + "/")).TextAsync();
        await session.HooksLoadedAsync();

        Assert.Contains("data-rask-hooks data-rask-managed></script></body>", firstResponse, StringComparison.Ordinal);
        Assert.Equal(1, await session.HookBundleRequestsAsync());
    }

    [Fact]
    public async Task An_arrow_focuses_and_selects_the_next_tab_past_a_disabled_one_and_around_the_ends()
    {
        await using var session = await HookSession.OpenAsync<TabsHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();

        await page.FocusAsync("#one");
        await page.Keyboard.PressAsync("ArrowRight");
        var pastDisabled = await session.FocusAsync();
        await Expect(page.Locator("#selected")).ToHaveTextAsync("selected=three");
        await page.Keyboard.PressAsync("ArrowDown");
        var wrapped = await session.FocusAsync();
        await Expect(page.Locator("#selected")).ToHaveTextAsync("selected=one");
        await page.Keyboard.PressAsync("ArrowUp");
        await Expect(page.Locator("#selected")).ToHaveTextAsync("selected=three");
        await page.Keyboard.PressAsync("ArrowLeft");

        Assert.Equal("three", pastDisabled);
        Assert.Equal("one", wrapped);
        await Expect(page.Locator("#selected")).ToHaveTextAsync("selected=one");
        Assert.Equal("one", await session.FocusAsync());
        Assert.Equal("true", await page.GetAttributeAsync("#one", "aria-selected"));
        Assert.Equal(0, await page.EvaluateAsync<double>("() => scrollY"));
    }

    [Fact]
    public async Task Home_and_End_stay_the_pages_and_an_arrow_on_the_action_button_starts_from_the_selected_tab()
    {
        await using var session = await HookSession.OpenAsync<TabsHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.EvaluateAsync("() => { window.keys = []; document.addEventListener('keydown', e => window.keys.push(e.key + (e.defaultPrevented ? '!' : ''))); }");

        await page.FocusAsync("#one");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Home");
        var afterHomeAndEnd = await session.FocusAsync();
        await page.FocusAsync("#add");
        await page.Keyboard.PressAsync("ArrowRight");

        Assert.Equal("one", afterHomeAndEnd);
        Assert.Equal("three", await session.FocusAsync());
        await Expect(page.Locator("#selected")).ToHaveTextAsync("selected=three");
        await Expect(page.Locator("#added")).ToHaveTextAsync("added=0");
        Assert.Equal("End Home ArrowRight!", await page.EvaluateAsync<string>("() => window.keys.join(' ')"));
    }
}

/// <summary>A tablist written by hand, selected in C#: three tabs, the middle one disabled, and an action.</summary>
public sealed partial class TabsHookPage : Component
{
    private string _selected = "one";
    private int _added;

    protected override Component? HeadAssets => Markup.Title["tab hooks"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("selected")[$"selected={_selected}"],
        P.Id("added")[$"added={_added}"],
        Div.Role("tablist")[
            Tab("one", disabled: false),
            Tab("two", disabled: true),
            Tab("three", disabled: false),
            Button.Key("add").Id("add").Type(ButtonType.Button).OnClick(() => _added++)["Add tab"]
        ],
        Div.Style("height:3000px")
    ];

    private Component Tab(string name, bool disabled)
    {
        var selected = string.Equals(_selected, name, StringComparison.Ordinal);
        return Button
            .Key(name)
            .Id(name)
            .Role("tab")
            .TabIndex(selected ? 0 : -1)
            .Aria("selected", selected ? "true" : "false")
            .Type(ButtonType.Button)
            .Disabled(disabled)
            .OnClick(() => _selected = name)[name];
    }
}
