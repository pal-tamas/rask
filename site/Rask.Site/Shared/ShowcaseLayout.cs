using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Html.Components;
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

    // The field's accessible name. Its placeholder is not one: a placeholder disappears the moment
    // typing starts, taking the field's only description with it.
    private static readonly IReadOnlyDictionary<string, string?> FilterAria =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["label"] = "Filter guides and examples",
        };

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
        // The top bar is the kit's, so the showcase, the operator console and the landing site share one
        // piece of chrome rather than three near-identical ones. What is NOT the kit's is the sidebar
        // below: UiNav is a five-tab bar for a console, and this app has eighty guides in a filterable,
        // grouped rail. Forcing one into the other would have been worse than sharing neither.
        Nav.Class(
            "app-navbar sticky top-0 z-40 flex items-center gap-3 border-b border-base-300 "
            + "bg-base-100 px-3 py-2 text-base-content")[
            Button
                .Type("button")
                .Class(
                    "hamburger-btn inline-flex min-h-11 items-center rounded-lg px-2 "
                    + "text-base-content/70 hover:bg-base-200 hover:text-base-content md:hidden")
                .Aria(DrawerAria)
                .OnClick(() => _drawerOpen = !_drawerOpen)[
                UiIcon.Name(_drawerOpen ? UiIconName.Close : UiIconName.Menu).Class("size-5 shrink-0")
            ],
            NavLink
                .Href(Features.Routes.GuidesIndexPage())
                .ActiveClass("")
                .Class("app-brand font-semibold inline-flex min-w-0 items-center gap-2 text-base-content no-underline")[
                RaskLogo.Size(24).GradientId("brandBolt"),
                Span["Rask"],
                // Both badges are hidden below sm. The bar carries a hamburger, the brand, a GitHub
                // link and the theme picker, and on a 390px screen the row measured 399px — a
                // 9px overflow that scrolled the whole document sideways on every page of the docs.
                Span.Class("rask-badge hidden rounded-full border border-base-300 bg-base-200 px-2 py-0.5 text-xs text-base-content/70 sm:inline")["showcase"],
                Span.Class("hidden rounded-full border border-base-300 bg-base-200 px-2 py-0.5 text-xs text-base-content/70 sm:inline")[$"v{RaskVersion.Current}"]
            ],
            Div.Class("flex items-center gap-2 ms-auto")[
                PathDisplay,
                A
                    .Href("https://github.com/pal-tamas/rask")
                    .Target("_blank")
                    .Rel("noopener")
                    .Class(TopAction + " border border-base-300 bg-base-100 text-base-content hover:bg-base-200")[
                    UiIcon.Name(UiIconName.Star).Class("size-4 shrink-0"),
                    Span.Class("hidden sm:inline")["GitHub"]
                ]
                ,
                // The light/dark toggle that used to sit here went when the showcase became light on
                // the kit's palette: there was no second theme to flip to. There are thirty-five now,
                // so it comes back as the whole set — and still with no IJSRuntime, because daisyUI
                // matches the checked radio in CSS rather than asking a script to swap a class.
                UiThemeDropdown.Placement("dropdown-end")
            ]
        ],
        Div.Class("flex app-shell")[
            // Always in the flow from md up; below that it slides over the page, and a backdrop
            // closes it. The open state was already Rask state — the drawer never needed script.
            Aside
                // FULLSCREEN below md. It used to be a 288px (w-72) rail pinned to the left edge, which
                // left a strip of the page showing behind the backdrop and gave the list barely half a
                // phone to lay eighty guides out in. inset-0 with a full width is the whole viewport, so
                // the drawer is the only thing on screen while it is open and the list gets the height.
                .Class(_drawerOpen
                    ? "side-nav flex fixed inset-0 z-50 w-full bg-base-100 p-4 "
                      + "shadow-xl md:static md:inset-auto md:z-auto md:w-64 md:shadow-none"
                    : "side-nav hidden w-64 p-4 md:flex")[
                SidebarBody()
            ],
            _drawerOpen
                ? Div
                    .Class("nav-backdrop fixed inset-0 z-40 bg-black/40 md:hidden")
                    .OnClick(() => _drawerOpen = false)
                : null,
            Main.Class("grow py-4 px-3 md:px-5 page-main")[
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
            // The kit's search field, which is now an IFormControl<string> like every other control in
            // it — so this reads as a field rather than as a hand-assembled label/icon/input sandwich.
            //
            // OnInput, not OnChange: this filter narrows the list as it is typed. OnChange is the commit
            // moment (blur or Enter) and is what the operator console's searches use, since those
            // navigate. Block, because the rail is 256px and the field's default settles at 288 from sm
            // up.
            UiSearch
                .Value(_filter)
                .Placeholder("Filter guides & examples…")
                .AccessibleLabel("Filter guides and examples")
                .OnInput(v => _filter = v)
                .Size(UiSize.Sm)
                .Block(true)
                .Class("side-nav-filter")
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
            foreach (var (group, items) in GroupByName(links))
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
        foreach (var (group, _) in GroupByName(GuidesNav()))
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

    // Groups links by their Group label, in the order each group FIRST appears (the sidebar shows
    // groups in the order the flat list introduces them). See the body for why this is by name rather
    // than by consecutive run - it is the fix for the Examples section drawing "PWA" twice.
    private static IEnumerable<(string Group, List<(string Path, string Label, UiIconName Icon, string Group, string? MatchPrefix)> Items)>
        GroupByName(IEnumerable<(string Path, string Label, UiIconName Icon, string Group, string? MatchPrefix)> links)
    {
        // BY NAME, not by consecutive run, and the difference is a bug the Examples section shipped.
        //
        // The entries arrive in DI registration order, and Program.cs registers "PWA", then "Islands",
        // then six "UI kit", then twelve more "PWA". Grouped by run that is TWO "PWA" blocks: the
        // sidebar drew the heading twice, both derived the same GroupKey so one chevron opened and
        // closed both, and two sibling <li> carried the same Key - which RASK022 holds a keyed list to
        // as identity, leaving reconciliation between them undefined.
        //
        // First appearance decides position, so input that is already consecutive (the guide catalog,
        // authored in order) groups exactly as it did before.
        var order = new List<string>();
        var buckets =
            new Dictionary<string, List<(string Path, string Label, UiIconName Icon, string Group, string? MatchPrefix)>>(
                StringComparer.Ordinal);

        foreach (var link in links)
        {
            if (!buckets.TryGetValue(link.Group, out var bucket))
            {
                bucket = [];
                buckets.Add(link.Group, bucket);
                order.Add(link.Group);
            }

            bucket.Add(link);
        }

        foreach (var group in order)
        {
            yield return (group, buckets[group]);
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
