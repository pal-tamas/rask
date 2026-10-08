using Rask.Core.Components;
using Rask.Core.Routing;

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
    // The id the sidebar and the hamburger share: the hamburger is a label for the sidebar's own checkbox.
    private const string SidebarId = "docs-sidebar";

    // MatchPrefix: optional section prefix for parameterised links. When set, the
    // sidebar entry stays highlighted for any URL under that prefix (e.g. switching
    // /realtime/BTC ↔ /realtime/ETH keeps "Live ticker" active). Null means
    // exact-match only.
    private static readonly (string Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)[] Links =
    [
        // Paths are type-safe, generator-emitted route URLs (Routes.*) — RouteUrl converts
        // implicitly to the string Path slot, so a renamed/removed [Route] is a compile error here, not a
        // dead link. MatchPrefix stays a bare string (it is a URL prefix, not a whole route).
        (Routes.TodosPage(), "Todos", Ui.IconName.CheckCircle, "Apps", null)
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
    protected override async Task OnMount()
    {
        route.Changed += OnRouteChanged;
        OpenGuideGroups();
        OpenActiveGroup();
    }

    protected override async Task OnUnmount() => route.Changed -= OnRouteChanged;

    private void OnRouteChanged(object? sender, EventArgs e)
    {
        _drawerOpen = false;
        OpenActiveGroup();
        StateHasChanged();
    }

    protected override Component? Render() =>
    [
        // THE LANDING PAGE'S BAR, not one shaped like it. This was daisyUI's `navbar` with its
        // navbar-start / navbar-end halves, a second brand mark in the display face, a "showcase" pill,
        // a version badge, a live `path:` readout and a bordered ★ GitHub button; `/` had a <header>, a
        // bolt, a wordmark and three quiet text links. Both were defensible and neither was the other,
        // and a visitor crossing from `/` to `/docs` was the only person positioned to notice — which is
        // exactly who did.
        //
        // What is NOT shared is the sidebar below: `menu` covers the rail itself (see GroupBlock), but
        // nothing in the kit is a filterable, grouped, eighty-guide accordion, and forcing one into the
        // other would have been worse than sharing neither.
        //
        // The hamburger is the only thing the docs add, and it has to be in the bar because that is where
        // a thumb reaches for it. It stays the kit's sidebar toggle — a label for the sidebar's checkbox,
        // so the drawer opens on a prerendered page with no runtime, and a keyboard stop the runtime
        // presses on Enter/Space. size-11 over daisyUI's 2.5rem because 44px is the smallest reliable
        // touch target.
        SiteHeader
            .FullBleed(true)
            .Leading(Ui.SidebarToggle
                .For(SidebarId)
                .Collapsible(Ui.Breakpoint.Md)
                .AccessibleLabel("Toggle navigation")
                .Class("hamburger-btn size-11")),
        // The kit's sidebar: docked from md up, a drawer below it. The open state is the drawer's checkbox, mirrored
        // in _drawerOpen so a navigation closes it. The docked rail sits under the sticky top bar rather than
        // under its top edge, which is what the two arbitrary variants on the drawer say.
        //
        // The rail's own layout is utilities (#1101), where global.css's .side-nav rules used to be: a column that
        // does not scroll — the filter is pinned, the list below it scrolls — as wide as the old 280px column from md
        // up, and clearing the bar and the notch while it slides over the page below md. The widths are `!` because
        // the kit's panel carries its own default width and two width utilities would be settled by sheet order.
        Ui.Sidebar
            .Id(SidebarId)
            .Page(Main.Class(
                "grow min-w-0 px-3 py-4 pb-[calc(2rem_+_env(safe-area-inset-bottom))] md:px-5 page-main")[
                Div.Class("mx-auto max-w-[1280px] page-main-inner")[Outlet]
            ])
            .Collapsible(Ui.Breakpoint.Md)
            .Open(_drawerOpen)
            .OnToggle(open => { _drawerOpen = open; })
            .AccessibleLabel("Guides and examples")
            .CloseLabel("Close navigation")
            .Class("app-shell min-h-0 md:[&>.drawer-side]:top-(--nav-h) "
                   + "md:[&>.drawer-side]:h-[calc(100vh-var(--nav-h))]")
            .PanelClass("side-nav h-full flex-col gap-0 overflow-hidden border-ui-line bg-ui-bg px-3 py-4 w-72! "
                        + "pt-[calc(var(--nav-h)+env(safe-area-inset-top))] md:w-[280px]! md:pt-4")[
            SidebarBody()
        ]
    ];

    // The sidebar body is a non-scrolling flex column: a pinned filter header (.side-nav-search) over a
    // single scrolling list (.side-nav-scroll). The filter is a real flex header rather than a
    // position:sticky child because sticky-in-flexbox is unreliable in Safari (the filter would scroll
    // away with the list), and this keeps it rock-solid across browsers with a clean hairline divider.
    private Component SidebarBody() => [
        // Ui.SidebarHeader is the kit's own word for "holds its place while the list below scrolls", which is
        // exactly what this is. It brings the shrink-0; the rest is this sidebar's own look.
        Ui.SidebarHeader.Class("side-nav-search mb-1 block border-b border-ui-line bg-ui-well pb-2")[
            Ui.Input.Value(_filter).Label("Filter guides & examples…")
                .OnInput(v => _filter = v ?? "")
                .Placeholder("Filter guides & examples…").InputClass("side-nav-filter rounded-lg")
        ],
        Div.Class("side-nav-scroll flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto overscroll-contain")[
            Ui.Navlist[BuildSections()]
        ]
    ];

    // Guides-first: the narrative guides are the primary spine (top of the sidebar, groups expanded by
    // default via OpenGuideGroups), followed by the interactive Examples (the framework/core showcase
    // plus any host-contributed entries, e.g. the WASM PWA examples) and the Bootstrap-component
    // showcase — both demoted below the guides and collapsed until visited.
    private IEnumerable<(string Section, IEnumerable<(string Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)> Links)> Sections()
    {
        yield return ("Guides", GuidesNav());
        yield return ("Examples",
            Links.Concat(extraNav.Select(e => (e.Path, e.Label, e.Icon, e.Group, e.MatchPrefix))));
    }

    // The Guides section mirrors the GuideCatalog (docs/*.md rendered on-site), led by the index.
    private static IEnumerable<(string Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)> GuidesNav()
    {
        yield return (Routes.GuidesIndexPage(), "All guides", Ui.IconName.BookOpen, "Overview", null);
        foreach (var g in Features.GuideCatalog.All)
        {
            yield return (Routes.GuidePage(g.Slug), g.Title, g.Icon, g.Group, null);
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

            children.Add(Div.Class("side-nav-section mt-2 border-t border-ui-line px-2 pt-3 pb-1 font-mono text-[0.68rem] font-semibold uppercase tracking-[0.14em] text-ui-brand-ink first:mt-0 first:border-t-0")[section]);
            children.AddRange(groups);
        }

        if (children.Count == 0)
        {
            children.Add(Div.Class("side-nav-empty px-3 py-4 text-sm text-ui-muted")["Nothing matches that filter."]);
        }

        return children;
    }

    private Component GroupBlock(
        string key, string group, bool open,
        IReadOnlyList<(string Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)> items) =>
        // The kit's navlist group: Flux's disclosure, a <details> the browser opens and closes by itself, with
        // the open state handed back here so a navigation, or the filter, can open a group too.
        //
        // The nav-group and side-nav-link class names stay on the elements. They carry no styling — the kit
        // does that — but the unit and browser suites name them, and those assertions are still about the
        // right things: that a group reads as open, that the current link is the one for this page.
        Ui.NavlistGroup
            .Key(key)
            .Heading(group)
            .Expandable()
            .Expanded(open)
            .OnExpandedChange(now => SetGroup(key, now))
            .Class("nav-group")[
            // A closed group holds nothing: eighty guides are not written into every page for a reader who
            // opens three of them.
            !open
                ? []
                : items.Select(i =>
                {
                    // The kit's navlist item: a NavLink underneath, so the current page is worked out from the
                    // route and says so with aria-current="page" — the attribute the browser suite selects the
                    // current link by.
                    var item = Ui.NavlistItem
                        .Key(i.Path)
                        // The slashed URL the host serves: a bare href is a 301 for every crawler (#1057).
                        .Href(InApp(i.Path))
                        .Icon(i.Icon)
                        .Class("side-nav-link");

                    // A link that stands for a whole section says so itself: the route alone only knows its own page.
                    if (i.MatchPrefix is not null)
                    {
                        item = item.Current(IsActive(i.Path, i.MatchPrefix));
                    }

                    // No cast: the chain ends at the children indexer, so it is already a Component
                    // and Select infers the sequence — which is what the indexer wants.
                    return item[i.Label];
                })
        ];

    // These paths ARE routes of this app, kept as strings because a host contributes some of them. The page
    // type is what tells a kit link to navigate in place and to compare itself with the page being shown; a
    // bare string would be an ordinary link, handed to the browser.
    private static RouteUrl InApp(string path) => PageMeta.LinkTo(path) with { PageType = typeof(ShowcaseLayout) };

    private void SetGroup(string key, bool open)
    {
        if (open)
        {
            _openGroups.Add(key);
        }
        else
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
            if (links.FirstOrDefault(link => IsActive(link.Path, link.MatchPrefix)) is { Path: not null } active)
            {
                _openGroups.Add(GroupKey(section, active.Group));
                return;
            }
        }
    }

    private static string GroupKey(string section, string group) => $"{section}\u001f{group}";

    // Groups consecutive links by their Group label, preserving the array order (the sidebar shows
    // groups in the order their first item appears, exactly as the flat list was authored).
    private static IEnumerable<(string Group, List<(string Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)> Items)>
        GroupConsecutive(IEnumerable<(string Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)> links)
    {
        string? current = null;
        List<(string Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)>? bucket = null;

        foreach (var link in links)
        {
            if (!string.Equals(link.Group, current, StringComparison.Ordinal))
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
        if (href is "/")
        {
            return route.Path is "/" || string.IsNullOrEmpty(route.Path);
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
