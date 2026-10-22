using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A link to a list that reads before it can draw, in a real browser: the page switches at once — heading and
///     address — and the table follows when its read is back.
/// </summary>
public sealed class ArrivalShowsItsPageAtOnceTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_link_to_a_list_that_reads_first_shows_the_heading_at_once_then_the_table_and_Back_returns_once()
    {
        await using var session = await HookSession.OpenAsync<ArrivalWaitPage>(playwright);
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
        Assert.True(headingAlone);
        Assert.Equal(entriesBefore + 1, entriesAfter);
        Assert.EndsWith("/", page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_link_to_a_slow_page_leaves_the_page_the_reader_was_on_at_once_and_its_table_follows()
    {
        await using var session = await HookSession.OpenAsync<ArrivalWaitPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#to-slow");
        var switchedBeforeTheRead = await page.WaitForFunctionAsync(
            "() => !!document.getElementById('heading') && !document.getElementById('home') && !document.getElementById('table')");
        await Expect(page.Locator("#table")).ToHaveTextAsync("rows:3");

        Assert.True(await switchedBeforeTheRead.JsonValueAsync<bool>());
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
            // Long enough that the page is seen without its table before the read is back.
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
