using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The hook that brings something back into view when a control inside a pager is pressed
///     (<c>data-rask-scroll-to</c>), in a real browser.
/// </summary>
/// <remarks>
///     It stands in for the <c>scroll-to</c> prop of Flux UI's pagination: a pager at the foot of a long table
///     changes the rows and would leave the reader at the bottom of the new page. The press is never stopped,
///     so the page's own handler still runs.
/// </remarks>
public sealed class RuntimeHookScrollTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string ToTheFoot = "() => scrollTo(0, document.documentElement.scrollHeight)";
    private const string TableTop = "() => Math.round(document.getElementById('orders').getBoundingClientRect().top)";

    [Fact]
    public async Task A_press_on_a_button_inside_the_pager_scrolls_its_target_into_view_and_still_runs_the_handler()
    {
        await using var session = await HookSession.OpenAsync<ScrollHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync(ToTheFoot);
        var before = await page.EvaluateAsync<int>(TableTop);

        await page.ClickAsync("#next");

        await page.WaitForFunctionAsync("() => document.getElementById('page').textContent === 'page=2'", null, new() { Timeout = 5_000 });
        Assert.True(before < 0, $"the table should start above the viewport, was at {before}");
        Assert.Equal(0, await page.EvaluateAsync<int>(TableTop));
    }

    [Fact]
    public async Task A_press_on_the_pager_that_is_on_no_button_or_link_scrolls_nothing()
    {
        await using var session = await HookSession.OpenAsync<ScrollHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync(ToTheFoot);
        var before = await page.EvaluateAsync<int>(TableTop);

        await page.ClickAsync("#summary");

        Assert.Equal(before, await page.EvaluateAsync<int>(TableTop));
    }

    [Fact]
    public async Task A_value_that_is_no_selector_scrolls_nothing_and_lets_the_press_through()
    {
        await using var session = await HookSession.OpenAsync<ScrollHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync(ToTheFoot);
        var before = await page.EvaluateAsync<int>(TableTop);

        await page.ClickAsync("#broken-next");

        await page.WaitForFunctionAsync("() => document.getElementById('page').textContent === 'page=2'", null, new() { Timeout = 5_000 });
        Assert.Equal(before, await page.EvaluateAsync<int>(TableTop));
    }
}

/// <summary>A table two screens tall with a pager under it, and a second pager whose target is not a selector.</summary>
public sealed partial class ScrollHookPage : Component
{
    private int _page = 1;

    protected override Component? HeadAssets => Markup.Title["scroll hook"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Div.Style("height:300px")["above"],
        Div.Id("orders").Style("height:200vh")[P.Id("page")[$"page={_page}"]],
        Div.Attributes(("data-rask-scroll-to", "#orders"))[
            Span.Id("summary")["Showing 1 to 15 of 240 results"],
            Button.Id("next").OnClick(() => _page++)["Next"]
        ],
        Div.Attributes(("data-rask-scroll-to", "#orders[")).Style("height:60px")[
            Button.Id("broken-next").OnClick(() => _page++)["Next"]
        ]
    ];
}
