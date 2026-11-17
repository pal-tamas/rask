using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A URL per language on the Server host (#1278), in a real browser at a desktop and a phone width: the default
///     language at the root and German under <c>/de</c>, a link keeping the page's language, a switcher moving between
///     them, and a navigation the page makes keeping its prefix — all live, none of it a page load.
/// </summary>
public sealed class UrlLanguagesTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private Task<HookSession> OpenAsync(string path, int width) =>
        HookSession.OpenAsync<UrlLanguagesApp>(
            playwright,
            new BrowserNewContextOptions { ViewportSize = new() { Width = width, Height = 800 }, HasTouch = width < 640 },
            path: path,
            cultures: c =>
            {
                c.SupportedCultures.Add("en");
                c.SupportedCultures.Add("de");
                c.UrlPrefix = true;
            });

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task The_default_language_is_served_at_the_root_of_the_address(int width)
    {
        await using var session = await OpenAsync("/ule/about", width);
        var page = session.Page;

        await Expect(page.Locator("#title")).ToHaveTextAsync("about in en");

        Assert.Equal("en", await page.GetAttributeAsync("html", "lang"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task German_is_served_under_its_tag(int width)
    {
        await using var session = await OpenAsync("/de/ule/about", width);
        var page = session.Page;

        await Expect(page.Locator("#title")).ToHaveTextAsync("about in de");

        Assert.Equal("de", await page.GetAttributeAsync("html", "lang"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task A_link_clicked_on_a_german_page_stays_under_its_tag(int width)
    {
        await using var session = await OpenAsync("/de/ule/about", width);
        var page = session.Page;
        await MarkTheDocumentAsync(page);

        await page.ClickAsync("#to-contact");

        await Expect(page.Locator("#title")).ToHaveTextAsync("contact in de");
        Assert.Equal(session.BaseUrl + "/de/ule/contact", page.Url);
        Assert.True(await TheSameDocumentAsync(page));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task The_switcher_moves_the_page_between_its_languages(int width)
    {
        await using var session = await OpenAsync("/ule/about?tab=2", width);
        var page = session.Page;
        await MarkTheDocumentAsync(page);

        await page.ClickAsync("#in-de");
        await Expect(page.Locator("#title")).ToHaveTextAsync("about in de");
        var german = page.Url;
        var germanLang = await page.GetAttributeAsync("html", "lang");
        await page.ClickAsync("#in-en");
        await Expect(page.Locator("#title")).ToHaveTextAsync("about in en");

        Assert.Equal(session.BaseUrl + "/de/ule/about?tab=2", german);
        Assert.Equal("de", germanLang);
        Assert.Equal(session.BaseUrl + "/ule/about?tab=2", page.Url);
        Assert.Equal("en", await page.GetAttributeAsync("html", "lang"));
        Assert.True(await TheSameDocumentAsync(page));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task A_navigation_the_page_makes_keeps_its_prefix(int width)
    {
        await using var session = await OpenAsync("/de/ule/about", width);
        var page = session.Page;
        await MarkTheDocumentAsync(page);

        await page.ClickAsync("#go-contact");

        await Expect(page.Locator("#title")).ToHaveTextAsync("contact in de");
        Assert.Equal(session.BaseUrl + "/de/ule/contact", page.Url);
        Assert.True(await TheSameDocumentAsync(page));
        Assert.Empty(session.PageErrors);
    }

    // A value only this document holds: still there after a click, the click was a live navigation, not a page load.
    private static Task MarkTheDocumentAsync(IPage page) => page.EvaluateAsync("() => { window.__sameDocument = true; }");

    private static Task<bool> TheSameDocumentAsync(IPage page) => page.EvaluateAsync<bool>("() => window.__sameDocument === true");
}

public sealed partial class UrlLanguagesApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/ule/about")]
public sealed partial class UrlLanguagesAboutPage(RouteState route) : Component
{
    protected override Component? Render() =>
        Main[
            H1.Id("title")[$"about in {UICulture.Name}"],
            Nav[
                NavLink.Href(route.In("en")).Id("in-en")["English"],
                NavLink.Href(route.In("de")).Id("in-de")["Deutsch"]
            ],
            NavLink.Href(Routes.UrlLanguagesContactPage()).Id("to-contact")["Contact"],
            Button.Id("go-contact").OnClick(static () => Routes.UrlLanguagesContactPage().Go())["Go to contact"]
        ];
}

[Route("/ule/contact")]
public sealed partial class UrlLanguagesContactPage : Component
{
    protected override Component? Render() => H1.Id("title")[$"contact in {UICulture.Name}"];
}
