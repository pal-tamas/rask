using System.Text;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK014 // the opaque host is a test-defined component with no generated entry
#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A child that changes element — and the page in the outlet above all — is answered with a morph of the
///     parent it is in, in a real browser: nothing outside that parent is touched, and inside it the morph
///     keeps what a whole-page morph keeps.
/// </summary>
/// <remarks>
///     <para>
///         The layout is what a navigation used to send again: a menu that scrolls, with a group the reader
///         opened; a header with a field the server does not bind, holding what the reader typed; a breadcrumb
///         whose item is words on one page and a link on the other. None of that is the render's to know, so a
///         whole page morphed over it put the group and the field back as they were served.
///     </para>
///     <para>
///         <see cref="MorphGuardsPage" /> holds everything a morph must step around in ONE parent, beside a
///         child that changes element: what a foreign renderer mounted, a node a script added, a modal dialog
///         and a popover the reader opened, a checkbox whose state is the reader's, an inline script, a drawing.
///     </para>
/// </remarks>
public sealed class SubtreeMorphJourneyTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private async Task<(HookSession Session, SocketFrames Frames)> OpenAsync(string path, string ready)
    {
        SocketFrames? frames = null;
        var session = await HookSession.OpenAsync<MorphJourneyApp>(playwright, path: path, beforeLoad: page =>
        {
            frames = SocketFrames.Of(page);
            return Task.CompletedTask;
        });
        await Expect(session.Page.Locator("#heading")).ToHaveTextAsync(ready);
        await session.Page.WaitForFunctionAsync("() => window.__rask !== undefined || document.querySelector('[data-rask-root]') !== null");
        frames!.Clear();

        return (session, frames);
    }

    [Fact]
    public async Task Going_to_another_page_of_the_layout_keeps_what_was_typed_in_the_header_the_menus_scroll_and_its_open_group()
    {
        var (session, frames) = await OpenAsync("/shop/orders", "Orders");
        await using var _ = session;
        var page = session.Page;
        await page.ClickAsync("#group-summary");
        await page.EvaluateAsync("() => { document.getElementById('menu').scrollTop = 300; window.__search = document.getElementById('search'); }");
        await page.ClickAsync("#search");
        await page.Keyboard.TypeAsync("glock");

        await page.EvaluateAsync("() => document.getElementById('to-new').click()");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("New order");

        // Focus is not asserted: a navigation that arrives moves it to the page's content on purpose, for a
        // reader who cannot see that the page changed (focusAndAnnounceRoute), whatever the reply was.
        await Expect(page).ToHaveTitleAsync("New order | Shop");
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/shop/orders/new$"));
        await Expect(page.Locator("#search")).ToHaveValueAsync("glock");
        await Expect(page.Locator("#crumb-list a")).ToHaveTextAsync("Orders");
        Assert.True(await page.EvaluateAsync<bool>("() => window.__search === document.getElementById('search')"), "the header's field is not the node it was");
        Assert.Equal(300, await page.EvaluateAsync<int>("() => document.getElementById('menu').scrollTop"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.getElementById('group').open"), "the group the reader opened was closed");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
        Assert.Equal(2, frames.Morphs.Count);
        Assert.True(frames.LargestReply < 3_000, frames.Trace());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Back_and_forward_bring_each_page_and_its_title_again_and_still_leave_the_header_and_the_menu_alone()
    {
        var (session, frames) = await OpenAsync("/shop/orders", "Orders");
        await using var _ = session;
        var page = session.Page;
        await page.ClickAsync("#group-summary");
        await page.FillAsync("#search", "glock");
        await page.ClickAsync("#to-new");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("New order");

        await page.GoBackAsync();
        await Expect(page.Locator("#heading")).ToHaveTextAsync("Orders");
        var titleBack = await page.TitleAsync();
        var crumbBack = await page.Locator("#crumb-list").InnerHTMLAsync();
        await page.GoForwardAsync();
        await Expect(page.Locator("#heading")).ToHaveTextAsync("New order");

        await Expect(page).ToHaveTitleAsync("New order | Shop");
        await Expect(page.Locator("#search")).ToHaveValueAsync("glock");
        await Expect(page.Locator("#name")).ToHaveValueAsync(string.Empty);
        Assert.Equal("Orders | Shop", titleBack);
        Assert.StartsWith("<div>Orders</div>", crumbBack, StringComparison.Ordinal);
        Assert.True(await page.EvaluateAsync<bool>("() => document.getElementById('group').open"), "the group the reader opened was closed");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task A_button_that_arrived_with_the_page_runs_its_own_handler_and_the_one_after_the_outlet_still_runs_its_own()
    {
        var (session, frames) = await OpenAsync("/shop/orders", "Orders");
        await using var _ = session;
        var page = session.Page;

        await page.ClickAsync("#to-new");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("New order");
        await page.ClickAsync("#save");
        await page.ClickAsync("#after-outlet");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=2");
        await Expect(page.Locator("#after-count")).ToHaveTextAsync("after=1");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task One_element_in_place_of_another_beside_the_field_being_typed_in_leaves_its_focus_caret_and_text()
    {
        var (session, frames) = await OpenAsync("/shop/orders/new", "New order");
        await using var _ = session;
        var page = session.Page;
        await page.EvaluateAsync("() => { window.__name = document.getElementById('name'); }");

        await page.ClickAsync("#check-slowly");
        await page.ClickAsync("#name");
        await page.Keyboard.TypeAsync("Range one");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("ArrowLeft");
        await Expect(page.Locator("#status")).ToHaveTextAsync("taken");
        var caret = await page.EvaluateAsync<int>("() => document.getElementById('name').selectionStart");
        await page.Keyboard.TypeAsync("number ");

        await Expect(page.Locator("#name")).ToHaveValueAsync("Range number one");
        Assert.Equal("EM", await page.Locator("#status").EvaluateAsync<string>("status => status.tagName"));
        Assert.Equal("name", await session.FocusAsync());
        Assert.Equal(6, caret);
        Assert.True(await page.EvaluateAsync<bool>("() => window.__name === document.getElementById('name')"), "the field is not the node it was");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
        Assert.Single(frames.Morphs);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task A_morph_elsewhere_on_the_page_leaves_the_field_being_typed_in_with_its_focus_its_caret_and_its_text()
    {
        var (session, frames) = await OpenAsync("/shop/orders/new", "New order");
        await using var _ = session;
        var page = session.Page;
        await page.EvaluateAsync("() => { window.__search = document.getElementById('search'); }");

        await page.ClickAsync("#check-slowly");
        await page.ClickAsync("#search");
        await page.Keyboard.TypeAsync("Range one");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("ArrowLeft");
        await Expect(page.Locator("#status")).ToHaveTextAsync("taken");
        var caret = await page.EvaluateAsync<int>("() => document.getElementById('search').selectionStart");
        await page.Keyboard.TypeAsync("number ");

        await Expect(page.Locator("#search")).ToHaveValueAsync("Range number one");
        Assert.Equal("search", await session.FocusAsync());
        Assert.Equal(6, caret);
        Assert.True(await page.EvaluateAsync<bool>("() => window.__search === document.getElementById('search')"), "the field is not the node it was");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
        Assert.Single(frames.Morphs);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task A_shape_in_place_of_another_inside_an_svg_is_a_morph_of_the_svg_and_arrives_as_a_shape()
    {
        var (session, frames) = await OpenAsync("/shop/guards", "Guards");
        await using var _ = session;
        var page = session.Page;
        await page.EvaluateAsync("() => { window.__group = document.getElementById('group-in-drawing'); }");

        await page.ClickAsync("#reshape");
        await Expect(page.Locator("#drawing > rect")).ToHaveCountAsync(1);

        Assert.Equal(SvgNamespace, await page.EvaluateAsync<string>("() => document.querySelector('#drawing > rect').namespaceURI"));
        Assert.Equal("SVGRectElement", await page.EvaluateAsync<string>("() => document.querySelector('#drawing > rect').constructor.name"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__group === document.getElementById('group-in-drawing')"), "the group after the shape is not the node it was");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
        Assert.Single(frames.Morphs);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task A_morph_steps_around_everything_in_its_parent_that_is_not_the_renders_to_change()
    {
        var (session, frames) = await OpenAsync("/shop/guards", "Guards");
        await using var _ = session;
        var page = session.Page;
        await page.EvaluateAsync(
            """
            () => {
                document.getElementById('island').appendChild(Object.assign(document.createElement('b'), { id: 'mounted', textContent: 'mounted' }));
                const managed = Object.assign(document.createElement('i'), { id: 'managed', textContent: 'managed' });
                managed.setAttribute('data-rask-managed', '');
                document.getElementById('guards').insertBefore(managed, document.getElementById('island'));
                document.getElementById('kept').click();
                document.getElementById('dialog').showModal();
                document.getElementById('tip').showPopover();
                window.__after = document.getElementById('after');
            }
            """);
        var runsBefore = await page.EvaluateAsync<int>("() => window.__inline");

        await page.EvaluateAsync("() => document.getElementById('swap').click()");
        await Expect(page.Locator("#shape")).ToHaveTextAsync("div");
        await page.EvaluateAsync("() => document.getElementById('count').click()");

        await Expect(page.Locator("#counted")).ToHaveTextAsync("counted=1");
        await Expect(page.Locator("#mounted")).ToHaveTextAsync("mounted");
        await Expect(page.Locator("#managed")).ToHaveTextAsync("managed");
        Assert.True(await page.EvaluateAsync<bool>("() => document.getElementById('dialog').matches(':modal')"), "the modal dialog was closed");
        Assert.True(await page.EvaluateAsync<bool>("() => document.getElementById('tip').matches(':popover-open')"), "the popover was closed");
        Assert.True(await page.EvaluateAsync<bool>("() => document.getElementById('kept').checked"), "the reader's checkbox was reset");
        Assert.Equal(SvgNamespace, await page.EvaluateAsync<string>("() => document.getElementById('square').namespaceURI"));
        Assert.Equal(1, runsBefore);
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__inline"));
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__arrived"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__after === document.getElementById('after')"), "the node after the morphed parent is not the node it was");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
        Assert.Equal(["1,1,0,2,1"], frames.Morphs);
        Assert.Empty(session.PageErrors);
    }
}

/// <summary>A layout with a menu, a header and an outlet, and three pages under it.</summary>
public sealed partial class MorphJourneyApp : Component
{
    protected override Component? HeadAssets => Markup.Title["Shop"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

[Route("/shop")]
public sealed partial class MorphJourneyLayout(RouteState route) : Component
{
    private int _after;

    protected override Component? HeadAssets => Title[route.Title is { } title ? title + " | Shop" : "Shop"];

    protected override Component? Render() =>
        Div.Id("layout")[
            Nav.Id("menu").Style("display:block;height:120px;overflow-y:auto")[
                // Closed as rendered: the reader opens it, and no render after that knows.
                Details.Id("group")[
                    Summary.Id("group-summary")["Group"],
                    Enumerable.Range(1, 60).Select(n => (Component)Div.Key(n).Style("height:20px")[$"Menu item {n}"]).ToArray()
                ]
            ],
            Header[
                Input.Value(string.Empty).Id("search"),
                Div.Id("crumbs")[
                    Div.Key("list").Id("crumb-list")[
                        route.Title == MorphNewPage.Name ? NavLink.Href(Routes.MorphListPage())["Orders"] : Div["Orders"],
                        Span[">"]
                    ]
                ]
            ],
            Main.Id("main")[Outlet],
            Footer[Button.Id("after-outlet").OnClick(() => _after++)["after"], Span.Id("after-count")[$"after={_after}"]]
        ];
}

[Route("orders")]
[ParentRoute(typeof(MorphJourneyLayout))]
public sealed partial class MorphListPage : Component
{
    protected override string? PageTitle => "Orders";

    protected override Component? Render() =>
    [
        Div[H1.Id("heading")["Orders"], NavLink.Href(Routes.MorphNewPage()).Id("to-new")["New order"]],
        P.Id("rows")["Three orders."],
    ];
}

[Route("orders/new")]
[ParentRoute(typeof(MorphJourneyLayout))]
public sealed partial class MorphNewPage : Component
{
    internal const string Name = "New order";

    private bool _taken;
    private int _saved;

    protected override string? PageTitle => Name;

    protected override Component? Render() =>
    [
        H1.Id("heading")[Name],
        Fieldset[
            // An em in place of a span, before the field nothing binds.
            _taken ? Em.Id("status")["taken"] : Span.Id("status")["free"],
            Input.Value(string.Empty).Id("name"),
            Button.Id("check-slowly").OnClick(CheckSlowly)["check"],
            Button.Id("save").OnClick(() => _saved++)["save"]
        ],
        P.Id("saved")[$"saved={_saved}"],
    ];

    private async Task CheckSlowly()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        _taken = true;
    }
}

[Route("guards")]
[ParentRoute(typeof(MorphJourneyLayout))]
public sealed partial class MorphGuardsPage : Component
{
    private bool _swapped;
    private bool _reshaped;
    private int _counted;

    protected override string? PageTitle => "Guards";

    protected override Component? Render() =>
    [
        H1.Id("heading")["Guards"],
        Section.Id("guards")[
            _swapped ? Div.Id("shape")["div"] : Span.Id("shape")["span"],
            new MountedElsewhere(),
            Dialog.Id("dialog")["A dialog the reader opened."],
            Div.Id("tip").Popover(Popover.Manual)["A popover the reader opened."],
            Input.Value(string.Empty).Type(InputType.Checkbox).Id("kept").Attributes(("data-rask-persist", "subtree-morph-journey")),
            Script[Raw.Value("window.__inline = (window.__inline || 0) + 1;")],
            Svg[_swapped ? Rect.Id("square").Width("9").Height("9") : Circle.Id("dot").R("5"), G],
            _swapped ? Script[Raw.Value("window.__arrived = (window.__arrived || 0) + 1;")] : null,
            Button.Id("count").OnClick(() => _counted++)["count"]
        ],
        P.Id("after")[$"counted={_counted}"],
        Span.Id("counted")[$"counted={_counted}"],
        Button.Id("swap").OnClick(() => _swapped = true)["swap"],
        Div[Svg.Id("drawing")[_reshaped ? Rect.Width("9").Height("9") : Circle.R("5"), G.Id("group-in-drawing")]],
        Button.Id("reshape").OnClick(() => _reshaped = true)["reshape"],
    ];

    // An element whose children are another renderer's: the render knows it as empty.
    private sealed class MountedElsewhere : Component
    {
        protected override string? TagName => "rask-external";

        protected override bool OpaqueSubtree => true;

        protected override void WriteAttributes(StringBuilder sb) => AppendAttr(sb, "id", "island");
    }
}
