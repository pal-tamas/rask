using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A field that carries <c>autofocus</c> has focus when its page is reached in place, as it has when the
///     same address is loaded — and when it arrives on the page the reader is already on.
/// </summary>
/// <remarks>
///     A browser acts on <c>autofocus</c> when it loads a document; a navigation in an open session is a morph
///     of the document that is already there, so the runtime acts on it (<c>rask-autofocus.ts</c>). The route is
///     announced either way, a page without the attribute is focused where it always was, and a reader who is
///     typing is never interrupted.
/// </remarks>
public sealed class AutofocusJourneyTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private async Task<(HookSession Session, SocketFrames Frames)> OpenAsync(string path, string ready, Action<RaskLiveOptions>? live = null)
    {
        SocketFrames? frames = null;
        var session = await HookSession.OpenAsync<AutofocusJourneyApp>(playwright, path: path, live: live, beforeLoad: page =>
        {
            frames = SocketFrames.Of(page);
            return Task.CompletedTask;
        });
        await Expect(session.Page.Locator("#heading")).ToHaveTextAsync(ready);
        await session.Page.WaitForFunctionAsync("() => document.querySelector('.rask-route-announcer') !== null");
        frames!.Clear();

        return (session, frames);
    }

    [Fact]
    public async Task The_address_of_a_form_loaded_whole_focuses_its_autofocus_field()
    {
        var (session, _) = await OpenAsync("/desk/new", "New entry");
        await using var disposal = session;

        var focus = await session.FocusAsync();

        Assert.Equal("name", focus);
    }

    [Fact]
    public async Task A_link_to_a_form_focuses_its_autofocus_field_announces_the_page_and_typing_goes_into_the_field()
    {
        var (session, frames) = await OpenAsync("/desk/list", "Entries");
        await using var disposal = session;
        var page = session.Page;

        await page.ClickAsync("#to-new");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("New entry");
        await page.WaitForFunctionAsync("() => document.querySelector('.rask-route-announcer').textContent === 'New entry | Desk'");
        var focus = await session.FocusAsync();
        await page.Keyboard.TypeAsync("Ada");

        Assert.Equal("name", focus);
        await Expect(page.Locator("#name")).ToHaveValueAsync("Ada");
        Assert.False(await page.EvaluateAsync<bool>("() => document.getElementById('main').hasAttribute('tabindex')"), "the page's content was made focusable for a focus it never got");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
        Assert.Single(frames.Morphs);
    }

    [Fact]
    public async Task A_handler_that_goes_to_a_form_focuses_its_autofocus_field()
    {
        var (session, _) = await OpenAsync("/desk/list", "Entries");
        await using var disposal = session;
        var page = session.Page;

        await page.ClickAsync("#go-new");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("New entry");

        await Expect(page.Locator("#name")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task A_page_that_sends_the_reader_on_to_a_form_focuses_the_forms_autofocus_field()
    {
        var (session, _) = await OpenAsync("/desk/list", "Entries");
        await using var disposal = session;
        var page = session.Page;

        await page.ClickAsync("#to-guarded");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("New entry");

        await Expect(page.Locator("#name")).ToBeFocusedAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/desk/new$"));
    }

    [Fact]
    public async Task A_form_that_arrives_as_a_whole_page_focuses_its_autofocus_field()
    {
        var (session, frames) = await OpenAsync("/desk/list", "Entries", o => o.DiffMode = LiveDiffMode.DisabledFull);
        await using var disposal = session;
        var page = session.Page;

        await page.ClickAsync("#to-new");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("New entry");

        await Expect(page.Locator("#name")).ToBeFocusedAsync();
        Assert.True(frames.FullPages.Count > 0, frames.Trace());
    }

    [Fact]
    public async Task A_field_the_navigation_kept_and_only_marked_autofocus_is_focused_too()
    {
        var (session, frames) = await OpenAsync("/desk/twin-a", "Twin A");
        await using var disposal = session;
        var page = session.Page;
        await page.EvaluateAsync("() => { window.__twin = document.getElementById('twin'); }");

        await page.ClickAsync("#to-twin-b");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("Twin B");

        await Expect(page.Locator("#twin")).ToBeFocusedAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => window.__twin === document.getElementById('twin')"), "the field is not the node it was");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
        Assert.Empty(frames.Morphs);
    }

    [Fact]
    public async Task A_page_with_no_autofocus_field_is_focused_on_its_content_and_announced_as_before()
    {
        var (session, _) = await OpenAsync("/desk/list", "Entries");
        await using var disposal = session;
        var page = session.Page;

        await page.ClickAsync("#to-plain");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("Plain");
        await page.WaitForFunctionAsync("() => document.querySelector('.rask-route-announcer').textContent === 'Plain | Desk'");

        Assert.Equal("main", await session.FocusAsync());
    }

    [Fact]
    public async Task Back_to_a_form_focuses_its_autofocus_field_as_a_browser_going_back_to_a_document_does()
    {
        var (session, _) = await OpenAsync("/desk/new", "New entry");
        await using var disposal = session;
        var page = session.Page;
        await page.ClickAsync("#to-list");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("Entries");

        await page.GoBackAsync();
        await Expect(page.Locator("#heading")).ToHaveTextAsync("New entry");

        await Expect(page.Locator("#name")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task A_browser_going_back_to_a_document_focuses_its_autofocus_field()
    {
        // The platform's own answer, which the runtime's Back follows: two documents no runtime is on.
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.RouteAsync("http://probe.test/**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "text/html",
            Body = route.Request.Url.EndsWith("/form", StringComparison.Ordinal)
                ? "<!doctype html><title>form</title><a id=away href=/away>away</a><input id=first><input id=name autofocus>"
                : "<!doctype html><title>away</title><p id=away-page>away</p>",
        }));
        await page.GotoAsync("http://probe.test/form");
        // The browser gives the focus after the load, in a task of its own: waited for, then read.
        await Expect(page.Locator("#name")).ToBeFocusedAsync();
        var loaded = await page.EvaluateAsync<string>("() => document.activeElement.id");
        await page.ClickAsync("#away");
        await Expect(page.Locator("#away-page")).ToBeVisibleAsync();

        await page.GoBackAsync();
        await Expect(page.Locator("#name")).ToBeVisibleAsync();

        Assert.Equal("name", loaded);
        await Expect(page.Locator("#name")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task A_row_that_arrives_with_an_autofocus_field_is_focused_once_and_not_again_by_a_later_render()
    {
        var (session, _) = await OpenAsync("/desk/list", "Entries");
        await using var disposal = session;
        var page = session.Page;

        await page.ClickAsync("#add-row");
        await Expect(page.Locator("#row-1")).ToBeFocusedAsync();
        await page.ClickAsync("#bump");
        await Expect(page.Locator("#bumped")).ToHaveTextAsync("bumped=1");

        Assert.Equal("bump", await session.FocusAsync());
        await Expect(page.Locator("#row-1")).ToHaveAttributeAsync("autofocus", string.Empty);
    }

    [Fact]
    public async Task A_row_that_arrives_while_the_reader_types_in_another_field_leaves_the_reader_there()
    {
        var (session, _) = await OpenAsync("/desk/list", "Entries");
        await using var disposal = session;
        var page = session.Page;

        await page.ClickAsync("#add-row-slowly");
        await page.ClickAsync("#search");
        await page.Keyboard.TypeAsync("glo");
        await Expect(page.Locator("#row-1")).ToBeVisibleAsync();
        await page.Keyboard.TypeAsync("ck");

        Assert.Equal("search", await session.FocusAsync());
        await Expect(page.Locator("#search")).ToHaveValueAsync("glock");
        await Expect(page.Locator("#row-1")).ToHaveValueAsync(string.Empty);
    }

    [Fact]
    public async Task A_section_that_appears_with_an_autofocus_field_is_focused()
    {
        var (session, _) = await OpenAsync("/desk/list", "Entries");
        await using var disposal = session;
        var page = session.Page;

        await page.ClickAsync("#show-section");

        await Expect(page.Locator("#section-field")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task A_modal_the_page_opens_is_focused_by_the_browser_alone_and_a_field_arriving_in_it_is_focused()
    {
        var (session, _) = await OpenAsync("/desk/list", "Entries");
        await using var disposal = session;
        var page = session.Page;

        await page.ClickAsync("#open-modal");
        await Expect(page.Locator("#modal-first")).ToBeVisibleAsync();
        var opened = await page.EvaluateAsync<string>("() => document.activeElement.hasAttribute('data-ui-focus-placeholder') ? 'placeholder' : document.activeElement.id || document.activeElement.tagName");
        await page.ClickAsync("#modal-more");

        Assert.Equal("placeholder", opened);
        await Expect(page.Locator("#modal-extra")).ToBeFocusedAsync();
    }
}

/// <summary>A desk: a layout with a field of its own, and pages with and without an <c>autofocus</c> field.</summary>
public sealed partial class AutofocusJourneyApp : Component
{
    protected override Component? HeadAssets => Markup.Title["Desk"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

[Route("/desk")]
public sealed partial class DeskLayout(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } title ? title + " | Desk" : "Desk"];

    protected override Component? Render() =>
        Div.Id("layout")[
            Header[Input.Value(string.Empty).Id("search")],
            Main.Id("main")[Outlet]
        ];
}

[Route("list")]
[ParentRoute(typeof(DeskLayout))]
public sealed partial class DeskListPage : Component
{
    private int _rows;
    private int _bumped;
    private bool _section;
    private bool _modal;
    private bool _more;

    protected override string? PageTitle => "Entries";

    protected override Component? Render() =>
    [
        H1.Id("heading")["Entries"],
        Nav[
            NavLink.Href(Routes.DeskNewPage()).Id("to-new")["New entry"],
            NavLink.Href(Routes.DeskGuardedPage()).Id("to-guarded")["Guarded"],
            NavLink.Href(Routes.DeskPlainPage()).Id("to-plain")["Plain"]
        ],
        Button.Id("go-new").OnClick(() => Routes.DeskNewPage().Go())["go"],
        Button.Id("add-row").OnClick(() => _rows++)["add"],
        Button.Id("add-row-slowly").OnClick(AddSlowly)["add slowly"],
        Button.Id("bump").OnClick(() => _bumped++)["bump"],
        Span.Id("bumped")[$"bumped={_bumped}"],
        Button.Id("show-section").OnClick(() => _section = true)["show"],
        Button.Id("open-modal").OnClick(() => _modal = true)["open"],
        Table[
            Tbody[
                Enumerable.Range(1, _rows).Select(n => (Component)Tr.Key(n)[Td[Input.Value(string.Empty).Id($"row-{n}").Autofocus(true)]]).ToArray()
            ]
        ],
        Div.Id("section-slot")[_section ? Section[Input.Value(string.Empty).Id("section-field").Autofocus(true)] : null],
        Ui.Modal.Open(_modal).OnClose(() => _modal = false)[
            Input.Value(string.Empty).Id("modal-first").Autofocus(true),
            Button.Id("modal-more").OnClick(() => _more = true)["more"],
            Div.Id("modal-slot")[_more ? Input.Value(string.Empty).Id("modal-extra").Autofocus(true) : null]
        ],
    ];

    private async Task AddSlowly()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        _rows++;
    }
}

[Route("new")]
[ParentRoute(typeof(DeskLayout))]
public sealed partial class DeskNewPage : Component
{
    protected override string? PageTitle => "New entry";

    protected override Component? Render() =>
    [
        H1.Id("heading")["New entry"],
        NavLink.Href(Routes.DeskListPage()).Id("to-list")["Entries"],
        Fieldset[
            Input.Value(string.Empty).Id("code"),
            Input.Value(string.Empty).Id("name").Autofocus(true)
        ],
    ];
}

[Route("plain")]
[ParentRoute(typeof(DeskLayout))]
public sealed partial class DeskPlainPage : Component
{
    protected override string? PageTitle => "Plain";

    protected override Component? Render() => [H1.Id("heading")["Plain"], P["Nothing here asks for focus."]];
}

// Always sends the reader on to the form.
[Route("guarded")]
[ParentRoute(typeof(DeskLayout))]
public sealed partial class DeskGuardedPage : Component
{
    protected override string? PageTitle => "Guarded";

    protected override Task OnMount()
    {
        Routes.DeskNewPage().Go();
        return Task.CompletedTask;
    }

    protected override Component? Render() => H1.Id("heading")["Guarded"];
}

// Two pages of one shape: going from one to the other changes words and attributes, and no node.
[Route("twin-a")]
[ParentRoute(typeof(DeskLayout))]
public sealed partial class DeskTwinAPage : Component
{
    protected override string? PageTitle => "Twin A";

    protected override Component? Render() =>
        Div[H1.Id("heading")["Twin A"], NavLink.Href(Routes.DeskTwinBPage()).Id("to-twin-b")["other"], Input.Value(string.Empty).Id("twin")];
}

[Route("twin-b")]
[ParentRoute(typeof(DeskLayout))]
public sealed partial class DeskTwinBPage : Component
{
    protected override string? PageTitle => "Twin B";

    protected override Component? Render() =>
        Div[H1.Id("heading")["Twin B"], NavLink.Href(Routes.DeskTwinAPage()).Id("to-twin-b")["other"], Input.Value(string.Empty).Id("twin").Autofocus(true)];
}
