using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Ui;

namespace Rask.Site;

// Rooted at /docs, not /, and that is what makes one app out of what used to be two.
//
// The landing page and the showcase were separate WASM apps, published to / and /docs/ and stitched
// together by the Pages workflow. Merged into one app they both wanted /, which is a real collision
// (RASK031) rather than a technicality: a page at a layout's own prefix leaves which one renders
// arbitrary. Rooting the layout where the showcase was already published resolves it and costs
// nothing — every rask.sh/docs/... URL, every guide link and every reference in the docs still
// resolves to the same page it did before the merge.
[Route("/docs")]
public sealed partial class ShowcaseLayout(RouteState route, IEnumerable<ShowcaseNavEntry> extraNav)
    : Component
{
    private static readonly IReadOnlyDictionary<string, string?> DrawerAria =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = "Toggle navigation" };

    /// <summary>The shape of the two actions in the top bar's trailing edge.</summary>
    /// <remarks>
    /// <c>min-h-11</c> below <c>sm</c>: 44px is the smallest reliable touch target, and these are
    /// <c>text-sm</c>. The height relaxes from <c>sm</c> up, where there is a pointer.
    /// </remarks>
    private const string TopAction =
        "inline-flex min-h-11 items-center gap-1.5 rounded-lg px-3 text-sm font-medium no-underline "
        + "transition-colors sm:min-h-0 sm:py-1.5";

    // MatchPrefix: optional section prefix for parameterised links. When set, the
    // sidebar entry stays highlighted for any URL under that prefix (e.g. switching
    // /realtime/BTC ↔ /realtime/ETH keeps "Live ticker" active). Null means
    // exact-match only.
    private static readonly (string Path, string Label, UiIconName Icon, string Group, string? MatchPrefix)[] Links =
    [
        // Paths are type-safe, generator-emitted route URLs (Features.Routes.*) — RouteUrl converts
        // implicitly to the string Path slot, so a renamed/removed [Route] is a compile error here, not a
        // dead link. MatchPrefix stays a bare string (it is a URL prefix, not a whole route).
        (Features.Routes.TodosPage(), "Todos", UiIconName.CheckCircle, "Apps", null)
        // Many example pages are now folded into their guides as inline live demos: HttpClient+DI /
        // upload / download → HTTP & files (docs/http-and-files.md); typed browser-API wrappers → Browser
        // APIs (docs/browser-apis.md); Events + Toast messages → Composition (docs/composition.md); the
        // User & auth → Authentication (docs/authentication.md); User components → Getting started
        // (docs/getting-started.md); Live ticker → Lifecycle (docs/lifecycle.md); Master-detail →
        // Composition (docs/composition.md keyed lists). See DemoRegistry.
    ];

    // Mobile drawer open state (ignored at ≥md, where the responsive offcanvas is static), the
    // search filter text, and the set of expanded sidebar groups (keyed by section + group). All
    // three are plain component fields toggled through the live diff — no JS.
    private bool _drawerOpen;
    private string _filter = "";
    private readonly HashSet<string> _openGroups = new(StringComparer.Ordinal);

    // Subscribe to RouteState.Changed so the sidebar's active-class computation refreshes on every
    // nav (including browser back/forward), the mobile drawer closes after navigating, and the group
    // holding the active route auto-expands. NavLink does its own active styling; this only drives the
    // drawer/expand side effects and the layout re-render.
    protected override void OnMount()
    {
        route.Changed += OnRouteChanged;
        OpenGuideGroups();
        OpenActiveGroup();
    }

    protected override void OnUnmount() => route.Changed -= OnRouteChanged;

    private void OnRouteChanged()
    {
        _drawerOpen = false;
        OpenActiveGroup();
        StateHasChanged();
    }

    protected override Component? Render() =>
    [
        // daisyUI's `navbar`, with its `navbar-start` / `navbar-end` halves, so the showcase, the
        // operator console and the landing site share one piece of chrome rather than three
        // near-identical ones. What is NOT daisyUI's is the sidebar below: `menu` covers the rail
        // itself (see GroupBlock), but nothing in the kit is a filterable, grouped, eighty-guide
        // accordion, and forcing one into the other would have been worse than sharing neither.
        //
        // `app-navbar` now names the bar and styles nothing: the rule that used to back it —
        // `background: rgba(20, 16, 31, .82)`, a leftover from the dark-first showcase — sat UNLAYERED
        // in global.css, which outranks every layered utility on the page, so it beat the `bg-ui-bg`
        // written right here. The bar rendered near-black while its own text stayed `text-ui-ink`
        // (base-content, near-black in this light theme): the wordmark, the "showcase" pill and the
        // route readout all came out at about 1.4:1, on markup whose class names were entirely correct.
        // The glass survives — `bg-ui-bg/85` + a backdrop blur — but drawn from the palette, and it now
        // matches the landing page's header exactly, which is the same three utilities.
        Nav.Class(
            "app-navbar navbar sticky top-0 z-50 flex-nowrap gap-3 border-b border-ui-line "
            + "bg-ui-bg/85 px-3 pt-[calc(0.5rem_+_env(safe-area-inset-top))] text-ui-ink "
            + "backdrop-blur backdrop-saturate-150")[
            // w-auto/grow rather than daisyUI's 50/50 split: the leading half is a hamburger and a
            // wordmark and the trailing half is three controls, so an even split would squeeze the
            // wider one at exactly the width where it matters.
            Div.Class("navbar-start w-auto min-w-0 gap-2")[
                Button
                    .Type("button")
                    // btn-ghost/btn-square, not a hand-rolled transparent button: the CSS behind
                    // `hamburger-btn` forced `color: #fff` for the dark bar above and is gone with it.
                    // size-11 over daisyUI's 2.5rem because 44px is the smallest reliable touch target.
                    .Class("hamburger-btn btn btn-ghost btn-square size-11 md:hidden")
                    .Aria(DrawerAria)
                    .OnClick(() => _drawerOpen = !_drawerOpen)[
                    UiIcon.Name(_drawerOpen ? UiIconName.Close : UiIconName.Menu).Class("size-5 shrink-0")
                ],
                NavLink
                    .Href(Features.Routes.GuidesIndexPage())
                    .ActiveClass("")
                    .Class("app-brand font-semibold inline-flex min-w-0 items-center gap-2 text-ui-ink no-underline")[
                    RaskLogo.Size(24).GradientId("brandBolt"),
                    Span["Rask"],
                    // Both badges are hidden below sm, in the markup and nowhere else. The bar carries a
                    // hamburger, the brand, a GitHub link and the theme picker, and on a 390px screen the
                    // row measured 399px — a 9px overflow that scrolled the whole document sideways on
                    // every page of the docs. global.css had its own `display: none` for the first badge
                    // under a 768px media query, which disagreed with `sm:` (640px) about where the line
                    // is and only ever hid one of the two; the utility is the one that decides now.
                    Span.Class("badge badge-sm badge-primary badge-soft hidden sm:inline-flex")["showcase"],
                    Span.Class("badge badge-sm badge-ghost hidden sm:inline-flex")[$"v{RaskVersion.Current}"]
                ]
            ],
            Div.Class("navbar-end w-auto grow gap-2")[
                PathDisplay,
                A
                    .Href("https://github.com/pal-tamas/rask")
                    .Target("_blank")
                    .Rel("noopener")
                    .Class(TopAction + " border border-ui-line bg-ui-bg text-ui-ink hover:bg-ui-well")[
                    UiIcon.Name(UiIconName.Star).Class("size-4 shrink-0"),
                    Span.Class("hidden sm:inline")["GitHub"]
                ]
                ,
                // The light/dark toggle that used to sit here went when the showcase became light on
                // the kit's palette: there was no second theme to flip to. There are thirty-five now,
                // so it comes back as the whole set — and the choice is REMEMBERED, across this
                // navigation and the next visit. The kit's CSS-only picker that stood here could not
                // do that: no script means nothing to persist with, and its radio renders unchecked
                // every pass, so a navigation put the theme straight back to the default. ThemeMenu
                // owns the value and hands it to the boot script, which is what actually holds it.
                ThemeMenu.Placement("dropdown-end")
            ]
        ],
        Div.Class("flex items-start app-shell")[
            // Always in the flow from md up; below that it slides over the page, and a backdrop
            // closes it. The open state was already Rask state — the drawer never needed script.
            // Below the bar, not over it: the drawer used to be z-50 against the bar's z-40, and the
            // only reason its close button stayed reachable was a `z-index: 1046` on .app-navbar in
            // global.css. With that magic number gone the three layers say the order themselves —
            // bar 50, drawer 40, backdrop 30.
            Aside
                .Class(_drawerOpen
                    ? "side-nav flex fixed inset-y-0 left-0 z-40 w-72 bg-ui-bg p-4 "
                      + "shadow-xl md:static md:z-auto md:w-64 md:shadow-none"
                    : "side-nav hidden w-64 p-4 md:flex")[
                SidebarBody()
            ],
            _drawerOpen
                ? Div
                    .Class("nav-backdrop fixed inset-0 z-30 bg-black/40 md:hidden")
                    .OnClick(() => _drawerOpen = false)
                : null,
            Main.Class(
                "grow min-w-0 px-3 py-4 pb-[calc(2rem_+_env(safe-area-inset-bottom))] md:px-5 page-main")[
                Div.Class("mx-auto page-main-inner")[Outlet]
            ]
        ]
    ];

    // The sidebar body is a non-scrolling flex column: a pinned filter header (.side-nav-search) over a
    // single scrolling list (.side-nav-scroll). The filter is a real flex header rather than a
    // position:sticky child because sticky-in-flexbox is unreliable in Safari (the filter would scroll
    // away with the list), and this keeps it rock-solid across browsers with a clean hairline divider.
    private Component SidebarBody() => [
        Div.Class("side-nav-search")[
            Input
                .Value(_filter)
                .OnInput(v => _filter = v ?? "")
                .Placeholder("Filter guides & examples…")
                .Class($"side-nav-filter {Tw.Input}")
        ],
        Div.Class("side-nav-scroll")[
            Ul.Class("menu menu-sm w-full flex-nowrap p-0")[BuildSections()]
        ]
    ];

    // Guides-first: the narrative guides are the primary spine (top of the sidebar, groups expanded by
    // default via OpenGuideGroups), followed by the interactive Examples (the framework/core showcase
    // plus any host-contributed entries, e.g. the WASM PWA examples) and the Bootstrap-component
    // showcase — both demoted below the guides and collapsed until visited.
    private IEnumerable<(string Section, IEnumerable<(string Path, string Label, UiIconName Icon, string Group, string? MatchPrefix)> Links)> Sections()
    {
        yield return ("Guides", GuidesNav());
        yield return ("Examples",
            Links.Concat(extraNav.Select(e => (e.Path, e.Label, e.Icon, e.Group, e.MatchPrefix))));
    }

    // The Guides section mirrors the GuideCatalog (docs/*.md rendered on-site), led by the index.
    private static IEnumerable<(string Path, string Label, UiIconName Icon, string Group, string? MatchPrefix)> GuidesNav()
    {
        yield return (Features.Routes.GuidesIndexPage(), "All guides", UiIconName.Book, "Overview", null);
        foreach (var g in Features.GuideCatalog.All)
        {
            yield return (Features.Routes.GuidePage(g.Slug), g.Title, g.Icon, g.Group, null);
        }
    }

    private List<Component> BuildSections()
    {
        var children = new List<Component>();
        var filtering = _filter.Length > 0;

        foreach (var (section, links) in Sections())
        {
            var groups = new List<Component>();
            foreach (var (group, items) in GroupConsecutive(links))
            {
                var visible = filtering
                    ? items.Where(i => i.Label.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList()
                    : items;
                if (visible.Count == 0)
                {
                    continue;
                }

                var key = GroupKey(section, group);
                // While filtering every matching group is forced open so results are always visible.
                var open = filtering || _openGroups.Contains(key);
                groups.Add(GroupBlock(key, group, open, visible));
            }

            if (groups.Count == 0)
            {
                continue;
            }

            children.Add(Li.Class("side-nav-section menu-title")[section]);
            children.AddRange(groups);
        }

        if (children.Count == 0)
        {
            children.Add(Li.Class("side-nav-empty menu-title")["Nothing matches that filter."]);
        }

        return children;
    }

    private Component GroupBlock(
        string key, string group, bool open,
        IReadOnlyList<(string Path, string Label, UiIconName Icon, string Group, string? MatchPrefix)> items) =>
        // daisyUI's menu, and a real <ul>/<li> tree rather than a stack of divs: that shape is what the
        // component styles, and it is also what tells a screen reader how many entries a group has and
        // which one it is on.
        //
        // The nav-group-* and side-nav-link class names stay on the elements. They carry no styling any
        // more — daisyUI does that — but 55 assertions across the unit and browser suites name them, and
        // those assertions are still about the right things: that a group reads as open, that the active
        // link is the one for this page. Renaming them would have turned a restyle into a rewrite of the
        // tests that prove the restyle works, which is how a conversion loses its own safety net.
        Li.Class("nav-group").Key(key)[
            Button
                .Type("button")
                .Class(open ? "nav-group-toggle open menu-dropdown-toggle menu-dropdown-show" : "nav-group-toggle menu-dropdown-toggle")
                .OnClick(() => ToggleGroup(key))[
                UiIcon.Name(open ? UiIconName.ChevronDown : UiIconName.ChevronRight).Class("nav-group-chevron size-3.5"),
                Span.Class("nav-group-label")[group]
            ],
            // menu-dropdown-show belongs on the SUBMENU, not only on the toggle. daisyUI hides the
            // list with `.menu :where(li > .menu-dropdown:not(.menu-dropdown-show)) { display: none }`
            // and carries the class on the toggle purely to rotate its chevron — so with it on the
            // button alone every group rendered and nothing inside one was ever visible.
            !open
                ? null
                : Ul.Class("nav-group-items menu-dropdown menu-dropdown-show")[
                    // No cast: the chain ends at the children indexer, so it is already a Component
                    // and Select infers the sequence — which is what the indexer wants.
                    items.Select(i =>
                    {
                        // The local is what makes string -> RouteUrl reachable: the conversion is
                        // defined on a string, not on a string?, so a null has to stay a null RouteUrl
                        // rather than be converted.
                        RouteUrl? match = null;
                        if (i.MatchPrefix is { } mp)
                        {
                            match = mp;
                        }

                        return Li.Key(i.Path)[
                            NavLink
                                .Href(i.Path)
                                .Match(match)
                                .ActiveMatch(i.MatchPrefix is null ? null : NavLinkMatch.Prefix)
                                // Both names, on purpose. menu-active is what daisyUI styles; active is
                                // NavLink's own default and what seventeen assertions across the unit and
                                // browser suites look for. Dropping either would cost the styling or the
                                // tests that prove the link is the one for this page.
                                .ActiveClass("active menu-active")
                                .Class("side-nav-link")[
                                UiIcon.Name(i.Icon).Class("me-2"),
                                Span[i.Label]
                            ]
                        ];
                    })
                ]
        ];

    private void ToggleGroup(string key)
    {
        if (!_openGroups.Add(key))
        {
            _openGroups.Remove(key);
        }
    }

    // Guides-first: the guide category groups start expanded so the narrative spine is visible on
    // landing (the interactive Examples/Bootstrap groups stay collapsed accordions until visited).
    private void OpenGuideGroups()
    {
        foreach (var (group, _) in GroupConsecutive(GuidesNav()))
        {
            _openGroups.Add(GroupKey("Guides", group));
        }
    }

    // Expands the group containing the active route so a deep link / back-forward lands with its
    // section open. Leaves any groups the user opened by hand untouched (this only adds).
    private void OpenActiveGroup()
    {
        foreach (var (section, links) in Sections())
        {
            foreach (var link in links)
            {
                if (IsActive(link.Path, link.MatchPrefix))
                {
                    _openGroups.Add(GroupKey(section, link.Group));
                    return;
                }
            }
        }
    }

    private static string GroupKey(string section, string group) => $"{section}{group}";

    // Groups consecutive links by their Group label, preserving the array order (the sidebar shows
    // groups in the order their first item appears, exactly as the flat list was authored).
    private static IEnumerable<(string Group, List<(string Path, string Label, UiIconName Icon, string Group, string? MatchPrefix)> Items)>
        GroupConsecutive(IEnumerable<(string Path, string Label, UiIconName Icon, string Group, string? MatchPrefix)> links)
    {
        string? current = null;
        List<(string Path, string Label, UiIconName Icon, string Group, string? MatchPrefix)>? bucket = null;

        foreach (var link in links)
        {
            if (link.Group != current)
            {
                if (bucket is not null)
                {
                    yield return (current!, bucket);
                }

                current = link.Group;
                bucket = [];
            }

            bucket!.Add(link);
        }

        if (bucket is not null)
        {
            yield return (current!, bucket);
        }
    }

    private bool IsActive(string href, string? matchPrefix = null)
    {
        if (href == "/")
        {
            return route.Path == "/" || string.IsNullOrEmpty(route.Path);
        }

        var trimmed = route.Path.TrimEnd('/');
        if (string.Equals(trimmed, href, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (matchPrefix is null)
        {
            return false;
        }

        var trimmedPrefix = matchPrefix.TrimEnd('/');
        return string.Equals(trimmed, trimmedPrefix, StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith(trimmedPrefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
