using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A move that changes only the query string takes the current history entry's place, as Livewire's
///     <c>#[Url]</c> does, so a live search leaves no Back stop at every pause; <c>.Pushing()</c> adds one.
/// </summary>
/// <remarks>
///     <c>history.length</c> is what a reader's Back button walks, so it is what is counted. The WebAssembly host
///     makes the same move from the same navigator: <c>QueryNavigationHistoryWasmTests</c> in
///     <c>Rask.Site.E2E.Tests</c>.
/// </remarks>
public sealed class QueryNavigationHistoryTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Length = "() => history.length";

    [Fact]
    public async Task A_live_search_pause_changes_the_address_and_adds_no_history_entry()
    {
        await using var session = await HookSession.OpenAsync<QueryHistoryPage>(playwright);
        var page = session.Page;
        var before = await page.EvaluateAsync<int>(Length);

        await page.FillAsync("#search", "ada");
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"\?q=ada$"));
        await page.FillAsync("#search", "adam");
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"\?q=adam$"));

        Assert.Equal(before, await page.EvaluateAsync<int>(Length));
    }

    [Fact]
    public async Task Pushing_adds_the_entry_and_Back_returns_to_the_page_before_it()
    {
        await using var session = await HookSession.OpenAsync<QueryHistoryPage>(playwright);
        var page = session.Page;
        var before = await page.EvaluateAsync<int>(Length);

        await page.ClickAsync("#next");
        await Expect(page.Locator("#page")).ToHaveTextAsync("page=2");
        var after = await page.EvaluateAsync<int>(Length);
        await page.GoBackAsync();

        Assert.Equal(before + 1, after);
        await Expect(page.Locator("#page")).ToHaveTextAsync("page=");
    }

    [Fact]
    public async Task A_change_of_path_still_adds_an_entry()
    {
        await using var session = await HookSession.OpenAsync<QueryHistoryPage>(playwright);
        var page = session.Page;
        var before = await page.EvaluateAsync<int>(Length);

        await page.ClickAsync("#other");
        await Expect(page.Locator("#where")).ToHaveTextAsync("path=/other");

        Assert.Equal(before + 1, await page.EvaluateAsync<int>(Length));
    }
}

internal sealed class Search
{
    public string Q { get; set; } = "";
}

/// <summary>A search field that moves the query as it is typed, a pager that pushes, and a link to another path.</summary>
public sealed partial class QueryHistoryPage(RouteState route) : Component
{
    private readonly Search _search = new();

    protected override Component? HeadAssets => Markup.Title["query history"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("where")[$"path={route.Path}"],
        P.Id("page")[$"page={route.Query["page"]}"],
        Input.Bind(() => _search.Q).Live().AfterBind(_ => { Go.With("q", _search.Q); }).Id("search"),
        Button.Id("next").OnClick(() => { Go.With("page", "2").Pushing(); })["next"],
        Button.Id("other").OnClick(() => { Go.To("/other"); })["other"]
    ];
}
