using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Html.Components;
using Rask.Ui;

namespace Rask.Example.Shared;

[Route("/")]
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
    private static readonly (string Path, string Label, IconName Icon, string Group, string? MatchPrefix)[] Links =
    [
        // Paths are type-safe, generator-emitted route URLs (Features.Routes.*) — RouteUrl converts
        // implicitly to the string Path slot, so a renamed/removed [Route] is a compile error here, not a
        // dead link. MatchPrefix stays a bare string (it is a URL prefix, not a whole route).
        (Features.Routes.TodosPage(), "Todos", IconName.Check2Square, "Apps", null)
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
            "app-navbar sticky top-0 z-40 flex items-center gap-3 border-b border-ui-line "
            + "bg-ui-bg px-3 py-2 text-ui-ink")[
            Button
                .Type("button")
                .Class(
                    "hamburger-btn inline-flex min-h-11 items-center rounded-lg px-2 text-ui-muted "
                    + "hover:bg-ui-well hover:text-ui-ink md:hidden")
                .Aria(DrawerAria)
                .OnClick(() => _drawerOpen = !_drawerOpen)[
                UiIcon.Name(_drawerOpen ? UiIconName.Close : UiIconName.Menu).Class("size-5 shrink-0")
            ],
            NavLink
                .Href(Features.Routes.GuidesIndexPage())
                .ActiveClass("")
                .Class("app-brand font-semibold inline-flex items-center gap-2 text-ui-ink no-underline")[
                RaskLogo.Size(24).GradientId("brandBolt"),
                Span["Rask"],
                Span.Class("rask-badge rounded-full border border-ui-line bg-ui-well px-2 py-0.5 text-xs text-ui-muted")["showcase"],
                Span.Class("rounded-full border border-ui-line bg-ui-well px-2 py-0.5 text-xs text-ui-muted")[$"v{RaskVersion.Current}"]
            ],
            Div.Class("flex items-center gap-2 ms-auto")[
                PathDisplay,
                // The live playground is a separate WASM sub-app (Roslyn compiles Rask C# in the browser),
                // deployed only to GitHub Pages alongside this showcase. This layout is shared by the
                // Server and WASM showcases (and runs locally), neither of which serves a /playground
                // route — so link to the one place it actually lives (absolute), opened in a new tab.
                A
                    .Href("https://rask.sh/playground/")
                    .Target("_blank")
                    .Rel("noopener")
                    .Class(TopAction + " bg-ui-ink text-ui-bg hover:bg-ui-ink/90")[
                    UiIcon.Name(UiIconName.Play).Class("size-4 shrink-0"), "Playground"
                ],
                A
                    .Href("https://github.com/pal-tamas/rask")
                    .Target("_blank")
                    .Rel("noopener")
                    .Class(TopAction + " border border-ui-line bg-ui-bg text-ui-ink hover:bg-ui-well")[
                    UiIcon.Name(UiIconName.Star).Class("size-4 shrink-0"), "GitHub"
                ]
                // The light/dark toggle is gone. The showcase is light now, on the palette Rask.Ui
                // declares, so there is no second theme to flip to — and with it went the only reason
                // this layout injected IJSRuntime at all.
            ]
        ],
        Div.Class("flex app-shell")[
            // Always in the flow from md up; below that it slides over the page, and a backdrop
            // closes it. The open state was already Rask state — the drawer never needed script.
            Aside
                .Class(_drawerOpen
                    ? "side-nav flex fixed inset-y-0 left-0 z-50 w-72 bg-ui-bg p-4 "
                      + "shadow-xl md:static md:z-auto md:w-64 md:shadow-none"
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
            Input
                .Value(_filter)
                .OnInput(v => _filter = v ?? "")
                .Placeholder("Filter guides & examples…")
                .Class($"side-nav-filter {Tw.Input}")
        ],
        Div.Class("side-nav-scroll")[BuildSections()]
    ];

    // Guides-first: the narrative guides are the primary spine (top of the sidebar, groups expanded by
    // default via OpenGuideGroups), followed by the interactive Examples (the framework/core showcase
    // plus any host-contributed entries, e.g. the WASM PWA examples) and the Bootstrap-component
    // showcase — both demoted below the guides and collapsed until visited.
    private IEnumerable<(string Section, IEnumerable<(string Path, string Label, IconName Icon, string Group, string? MatchPrefix)> Links)> Sections()
    {
        yield return ("Guides", GuidesNav());
        yield return ("Examples",
            Links.Concat(extraNav.Select(e => (e.Path, e.Label, e.Icon, e.Group, e.MatchPrefix))));
    }

    // The Guides section mirrors the GuideCatalog (docs/*.md rendered on-site), led by the index.
    private static IEnumerable<(string Path, string Label, IconName Icon, string Group, string? MatchPrefix)> GuidesNav()
    {
        yield return (Features.Routes.GuidesIndexPage(), "All guides", IconName.Book, "Overview", null);
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

            children.Add(Div.Class("side-nav-section")[section]);
            children.AddRange(groups);
        }

        if (children.Count == 0)
        {
            children.Add(Div.Class("side-nav-empty text-sm text-ui-muted")["Nothing matches that filter."]);
        }

        return children;
    }

    private Component GroupBlock(
        string key, string group, bool open,
        IReadOnlyList<(string Path, string Label, IconName Icon, string Group, string? MatchPrefix)> items) =>
        Div.Class("nav-group").Key(key)[
            Button
                .Type("button")
                .Class(open ? "nav-group-toggle open" : "nav-group-toggle")
                .OnClick(() => ToggleGroup(key))[
                Icon.Name(open ? IconName.ChevronDown : IconName.ChevronRight).Class("nav-group-chevron"),
                Span.Class("nav-group-label")[group]
            ],
            !open
                ? null
                : Div.Class("nav-group-items flex flex-col")[
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

                        return NavLink
                            .Key(i.Path)
                            .Href(i.Path)
                            .Match(match)
                            .ActiveMatch(i.MatchPrefix is null ? null : NavLinkMatch.Prefix)
                            .Class("side-nav-link")[
                            Icon.Name(i.Icon).Class("me-2"),
                            Span[i.Label]
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
    private static IEnumerable<(string Group, List<(string Path, string Label, IconName Icon, string Group, string? MatchPrefix)> Items)>
        GroupConsecutive(IEnumerable<(string Path, string Label, IconName Icon, string Group, string? MatchPrefix)> links)
    {
        string? current = null;
        List<(string Path, string Label, IconName Icon, string Group, string? MatchPrefix)>? bucket = null;

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
