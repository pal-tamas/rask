using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The hook for a time picker's list (<c>data-rask-time-list</c>), in a real browser, on a page a session drives.
/// </summary>
/// <remarks>
///     Recorded on Flux UI's live time picker page on 2026-10-14: opening lights the chosen time, or the first, and
///     brings it into view by the least movement; the pointer lights the row it is over and never scrolls the
///     list, and that row goes dark when the pointer leaves; the vertical arrows walk past a time that cannot be
///     picked and stop at the ends, ArrowUp from no row lands on the last; Enter picks the lit row; Home and
///     End do nothing; Tab closes. None of it asks the server anything (<c>KitDrawsWithoutAskingTests</c>).
/// </remarks>
public sealed class RuntimeHookTimeListTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Button = "#time";
    private const string List = "[data-ui-time-picker] [popover]";

    [Fact]
    public async Task Opening_lights_the_chosen_time_brings_it_into_view_and_names_it_on_the_button()
    {
        await using var session = await HookSession.OpenAsync<TimeListHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();

        await page.ClickAsync(Button);
        await Expect(page.Locator(List)).ToHaveAttributeAsync("data-open", string.Empty);

        var lit = await Lit(page);
        Assert.Equal("14:00", lit);
        Assert.Equal("true", await page.GetAttributeAsync(Button, "aria-expanded"));
        Assert.Equal(await page.GetAttributeAsync("[data-time='14:00']", "id"), await page.GetAttributeAsync(Button, "aria-activedescendant"));
        Assert.True(await InSightAsync(page, "14:00"));
    }

    [Fact]
    public async Task The_pointer_lights_the_row_it_is_over_without_scrolling_and_it_goes_dark_when_the_pointer_leaves()
    {
        await using var session = await HookSession.OpenAsync<TimeListHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.ClickAsync(Button);
        await page.WaitForFunctionAsync("s => document.querySelector(s).scrollTop > 0", List);
        var before = await ScrollTopAsync(page);
        var box = (await page.Locator(List).BoundingBoxAsync())!;

        // The last row the list shows, of which only a part is in sight under the pointer.
        await page.Mouse.MoveAsync(box.X + 40, box.Y + box.Height - 4, new() { Steps = 4 });
        await page.WaitForTimeoutAsync(300);
        var lit = await Lit(page);
        var after = await ScrollTopAsync(page);
        await page.Mouse.MoveAsync(5, 5, new() { Steps = 4 });

        Assert.NotEqual("14:00", lit);
        Assert.False(string.IsNullOrEmpty(lit));
        Assert.Equal(before, after);
        await Expect(page.Locator(List + " [data-active]")).ToHaveCountAsync(0);
        Assert.Null(await page.GetAttributeAsync(Button, "aria-activedescendant"));
    }

    [Fact]
    public async Task Closed_an_arrow_opens_the_list_open_one_walks_past_a_time_that_cannot_be_picked_and_Enter_picks()
    {
        await using var session = await HookSession.OpenAsync<TimeListHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.FocusAsync(Button);

        await page.Keyboard.PressAsync("ArrowDown");
        await Expect(page.Locator(List)).ToHaveAttributeAsync("data-open", string.Empty);
        await page.Keyboard.PressAsync("ArrowDown");
        var pastTheGap = await Lit(page);
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("End");
        var homeAndEnd = await Lit(page);
        await page.Keyboard.PressAsync("Enter");

        Assert.Equal("15:00", pastTheGap);
        Assert.Equal("15:00", homeAndEnd);
        await Expect(page.Locator("#picked")).ToHaveTextAsync("picked=15:00");
        await Expect(page.Locator(List)).Not.ToHaveAttributeAsync("data-open", string.Empty);
        Assert.Equal("time", await session.FocusAsync());
    }

    [Fact]
    public async Task With_no_row_lit_ArrowUp_lands_on_the_last_time_and_Tab_closes_the_list()
    {
        await using var session = await HookSession.OpenAsync<TimeListHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.ClickAsync(Button);
        var box = (await page.Locator(List).BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + 40, box.Y + 20, new() { Steps = 3 });
        await page.Mouse.MoveAsync(5, 5, new() { Steps = 3 });

        await page.Keyboard.PressAsync("ArrowUp");
        var last = await Lit(page);
        var shown = await InSightAsync(page, "23:30");
        await page.Keyboard.PressAsync("Tab");

        Assert.Equal("23:30", last);
        Assert.True(shown);
        await Expect(page.Locator(List)).Not.ToHaveAttributeAsync("data-open", string.Empty);
        await Expect(page.Locator("#picked")).ToHaveTextAsync("picked=14:00");
    }

    private static Task<string> Lit(Microsoft.Playwright.IPage page) =>
        page.EvaluateAsync<string>("s => document.querySelector(s + ' [data-active]')?.getAttribute('data-time') ?? ''", List);

    private static Task<double> ScrollTopAsync(Microsoft.Playwright.IPage page) =>
        page.EvaluateAsync<double>("s => document.querySelector(s).scrollTop", List);

    private static Task<bool> InSightAsync(Microsoft.Playwright.IPage page, string time) =>
        page.EvaluateAsync<bool>(
            "([s, t]) => { const l = document.querySelector(s).getBoundingClientRect(); const r = document.querySelector(s + ` [data-time='${t}']`).getBoundingClientRect(); return r.top >= l.top && r.bottom <= l.bottom; }",
            new[] { List, time });
}

/// <summary>A time picker with a chosen time far down its list, one time that cannot be picked, and what it picked.</summary>
public sealed partial class TimeListHookPage : Component
{
    private TimeOnly? _time = new TimeOnly(14, 0);

    protected override Component? HeadAssets => [Markup.Title["time list hooks"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("picked")[$"picked={_time:HH\\:mm}"],
        Div.Style("width:260px;margin:16px")[
            Ui.TimePicker.Value(_time).Id("time").Locale("en-US").Unavailable([new TimeOnly(14, 30)]).OnChange(t => _time = t)
        ],
        Div.Style("height:2000px")
    ];
}
