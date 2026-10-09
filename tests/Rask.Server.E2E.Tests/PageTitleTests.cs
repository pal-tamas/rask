using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A page's title in its layout's breadcrumb and in the tab, in a real browser (#1239): the last crumb is
///     never missing — not in the document as served, not across a navigation, not after a rename.
/// </summary>
/// <remarks>
///     <see cref="WatchCrumbsAsync" /> records the crumb and the tab title after every change to the document,
///     so a frame that showed the layout without its page's name would be in the list.
/// </remarks>
public sealed class PageTitleTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Seen = "() => window.__seen.filter((s, i, all) => i === 0 || all[i - 1] !== s)";

    [Fact]
    public async Task The_document_as_served_shows_the_crumb_and_the_tab_title_with_no_script_at_all()
    {
        await using var session = await HookSession.OpenAsync<CrumbApp>(
            playwright, new() { JavaScriptEnabled = false }, path: "/crumbs/3");
        var page = session.Page;

        var crumb = await page.Locator("#crumb").InnerTextAsync();
        var title = await page.TitleAsync();

        Assert.Equal("Third módosítása", crumb);
        Assert.Equal("Third módosítása | ESZAF", title);
    }

    [Fact]
    public async Task Going_from_the_list_to_a_record_and_renaming_it_never_shows_the_layout_without_a_crumb()
    {
        await using var session = await HookSession.OpenAsync<CrumbApp>(
            playwright, beforeLoad: WatchCrumbsAsync, path: "/crumbs");
        var page = session.Page;
        await Expect(page.Locator("#crumb")).ToHaveTextAsync("Viszonylatok");
        await page.EvaluateAsync("() => { window.__stayed = true; }");

        await page.ClickAsync("#edit-3");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("Third módosítása");
        await page.ClickAsync("#rename");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("Renamed módosítása");

        await Expect(page).ToHaveTitleAsync("Renamed módosítása | ESZAF");
        Assert.True(await page.EvaluateAsync<bool>("() => window.__stayed === true"), "the navigation reloaded the document");
        Assert.Equal(
            [
                "Third módosítása ~ Third módosítása | ESZAF",
                "Renamed módosítása ~ Renamed módosítása | ESZAF",
            ],
            await page.EvaluateAsync<string[]>(Seen));
        Assert.Empty(session.PageErrors);
    }

    // The crumb and the tab title, noted after every mutation once the served document has been parsed —
    // while it is still arriving the layout is there before its crumb, which is the parser and no frame.
    private static Task WatchCrumbsAsync(IPage page) =>
        page.AddInitScriptAsync(
            """
            window.__seen = [];
            new MutationObserver(() => {
                if (document.readyState === 'loading' || !document.getElementById('crumbs')) return;
                const crumb = document.getElementById('crumb');
                window.__seen.push((crumb ? crumb.textContent : '(none)') + ' ~ ' + document.title);
            }).observe(document, { subtree: true, childList: true, characterData: true });
            """);
}

/// <summary>An app whose layout shows the page's title as the last breadcrumb and in the tab.</summary>
public sealed partial class CrumbApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/crumbs")]
public sealed partial class CrumbLayout(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } t ? $"{t} | ESZAF" : "ESZAF"];

    protected override Component? Render() =>
    [
        Nav.Id("crumbs")[
            NavLink.Href(Routes.CrumbListPage()).Id("home")["Home"],
            route.Title is { } t ? Span.Id("crumb")[t] : null
        ],
        Main[Outlet],
    ];
}

[Route("")]
[ParentRoute(typeof(CrumbLayout))]
public sealed partial class CrumbListPage : Component
{
    protected override string? PageTitle => "Viszonylatok";

    protected override Component? Render() => NavLink.Href(Routes.CrumbEditPage(3)).Id("edit-3")["Third"];
}

[Route("{Id}")]
[ParentRoute(typeof(CrumbLayout))]
public sealed partial class CrumbEditPage : Component
{
    private string? _name;

    [RouteParam] public int Id { get; set; }

    protected override string? PageTitle => _name is { } name ? $"{name} módosítása" : null;

    protected override async Task OnUpdated() => _name = await Find(Id);

    protected override Component? Render() =>
    [
        H1.Id("heading")[PageTitle ?? "Nincs ilyen viszonylat"],
        Button.Id("rename").OnClick(() => _name = "Renamed")["rename"],
    ];

    // A store that answers at once, as an in-process query that finds its row cached does.
    private static Task<string?> Find(int id) => Task.FromResult<string?>(id == 3 ? "Third" : null);
}
