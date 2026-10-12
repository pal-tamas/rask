using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A link to a list that reads before it can draw, in a real browser: the reader keeps the page they are on
///     until the list has its rows, and gets it in one paint.
/// </summary>
/// <remarks>
///     The budget here is far longer than either read, so a page arrives because its read came back and never
///     because time ran out — the journeys do not depend on how fast the machine is.
/// </remarks>
public sealed class ArrivalWaitsForItsPageTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static void LongerThanTheReads(RaskServerOptions o) => o.NavigationQuiescenceTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_link_to_a_list_that_reads_first_never_shows_the_heading_without_the_table_and_Back_returns_once()
    {
        await using var session = await HookSession.OpenAsync<ArrivalWaitPage>(playwright, server: LongerThanTheReads);
        var page = session.Page;
        await page.EvaluateAsync(
            """
            () => {
                window.__headingAlone = false;
                new MutationObserver(() => {
                    if (document.getElementById('heading') && !document.getElementById('table')) window.__headingAlone = true;
                }).observe(document.documentElement, { childList: true, subtree: true, characterData: true });
            }
            """);
        var entriesBefore = await page.EvaluateAsync<int>("() => history.length");

        await page.ClickAsync("#to-list");
        await Expect(page.Locator("#table")).ToHaveTextAsync("rows:3");
        var headingAlone = await page.EvaluateAsync<bool>("() => window.__headingAlone");
        var entriesAfter = await page.EvaluateAsync<int>("() => history.length");
        await page.GoBackAsync();

        await Expect(page.Locator("#home")).ToHaveTextAsync("home");
        Assert.False(headingAlone);
        Assert.Equal(entriesBefore + 1, entriesAfter);
        Assert.EndsWith("/", page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task While_a_slow_page_is_awaited_the_reader_keeps_the_page_they_are_on_under_the_progress_bar()
    {
        await using var session = await HookSession.OpenAsync<ArrivalWaitPage>(playwright, server: LongerThanTheReads);
        var page = session.Page;

        await page.ClickAsync("#to-slow");
        var keptUnderTheBar = await page.WaitForFunctionAsync(
            "() => !!document.querySelector('.rask-pending[data-show]') && !!document.getElementById('home') && !document.getElementById('heading')");
        await Expect(page.Locator("#table")).ToHaveTextAsync("rows:3");

        Assert.True(await keptUnderTheBar.JsonValueAsync<bool>());
        await Expect(page.Locator(".rask-pending[data-show]")).ToHaveCountAsync(0);
        Assert.EndsWith("/slow", page.Url, StringComparison.Ordinal);
    }
}

/// <summary>A home page at <c>/</c>, and a list at <c>/list</c> and at <c>/slow</c> that reads before it draws its table.</summary>
public sealed partial class ArrivalWaitPage(RouteState route) : Component
{
    protected override Component? HeadAssets => Markup.Title["arrival wait"];

    protected override string? HtmlLang => "en";

    protected override Task OnMount()
    {
        route.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        route.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        NavLink.Href("/list").Id("to-list")["list"],
        NavLink.Href("/slow").Id("to-slow")["slow list"],
        route.Path switch
        {
            // Long enough that the progress bar, which appears after 300 ms, is up well before the page is.
            "/slow" => ArrivalWaitList.Key("slow").ReadsFor(TimeSpan.FromMilliseconds(1500)),
            "/list" => ArrivalWaitList.Key("list").ReadsFor(TimeSpan.FromMilliseconds(50)),
            _ => P.Id("home")["home"],
        }
    ];
}

public sealed partial class ArrivalWaitList : Component
{
    private int? _rows;

    public TimeSpan ReadsFor { get; set; }

    protected override async Task OnMount()
    {
        await Task.Delay(ReadsFor, CancellationToken);
        _rows = 3;
    }

    protected override Component? Render() =>
    [
        H1.Id("heading")["list"],
        _rows is { } rows ? Table.Id("table")[Tbody[Tr[Td[$"rows:{rows}"]]]] : null,
    ];
}
