using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Example.Shared;

[Route("/")]
public sealed class ShowcaseLayout(RouteState route, IEnumerable<ShowcaseNavEntry> extraNav) : Component
{
    // MatchPrefix: optional section prefix for parameterised links. When set, the
    // sidebar entry stays highlighted for any URL under that prefix (e.g. switching
    // /realtime/BTC ↔ /realtime/ETH keeps "Live ticker" active). Null means
    // exact-match only.
    private static readonly (string Path, string Label, string Icon, string Group, string? MatchPrefix)[] Links =
    [
        // Paths are type-safe, generator-emitted route URLs (Features.Routes.*) — RouteUrl converts
        // implicitly to the string Path slot, so a renamed/removed [Route] is a compile error here, not a
        // dead link. MatchPrefix stays a bare string (it is a URL prefix, not a whole route).
        (Features.Routes.HomePage(), "Welcome", "bi-house", "Start", null),
        (Features.Routes.TagsPage(), "Tag factories", "bi-code-slash", "DSL", null),
        (Features.Routes.PrimitivesPage(), "Primitives", "bi-asterisk", "DSL", null),
        (Features.Routes.PropsPage(), "Universal props", "bi-gear", "DSL", null),
        (Features.Routes.SvgPage(), "SVG", "bi-vector-pen", "DSL", null),
        (Features.Routes.ElementsTextPage(), "Text & inline", "bi-fonts", "HTML elements", null),
        (Features.Routes.ElementsGroupingPage(), "Grouping & lists", "bi-list-ul", "HTML elements", null),
        (Features.Routes.ElementsSectionsPage(), "Sections & headings", "bi-layout-text-window", "HTML elements", null),
        (Features.Routes.ElementsFormsPage(), "Form elements", "bi-ui-checks-grid", "HTML elements", null),
        (Features.Routes.ElementsTablesPage(), "Table elements", "bi-table", "HTML elements", null),
        (Features.Routes.ElementsMediaPage(), "Media & embedded", "bi-image", "HTML elements", null),
        (Features.Routes.ElementsInteractivePage(), "Interactive", "bi-hand-index", "HTML elements", null),
        (Features.Routes.ElementsMetadataPage(), "Document & metadata", "bi-file-earmark-code", "HTML elements", null),
        (Features.Routes.ComponentsPage(), "User components", "bi-boxes", "Components", null),
        (Features.Routes.RoutingPage(), "Routing", "bi-signpost-2", "Components", null),
        (Features.Routes.UserDetailPage("42"), "Route + query params", "bi-link-45deg", "Components", "/users"),
        (Features.Routes.NavigatorPage(), "Navigator", "bi-compass", "Components", null),
        (Features.Routes.LifecyclePage(), "Lifecycle", "bi-arrow-repeat", "Components", null),
        (Features.Routes.ToastPage(), "Toast", "bi-bell", "Components", null),
        (Features.Routes.FlashPage(), "Flash messages", "bi-megaphone", "Components", null),
        (Features.Routes.ElementRefPage(), "Element refs", "bi-bullseye", "Components", null),
        (Features.Routes.UserPage(), "User & auth", "bi-person-lock", "Components", null),
        (Features.Routes.LiveTickerPage("BTC"), "Live ticker", "bi-graph-up-arrow", "Components", "/realtime"),
        (Features.Routes.BackgroundServicePage(), "Background service", "bi-broadcast", "Components", null),
        (Features.Routes.CancellationPage(), "Cancellation", "bi-x-circle", "Components", null),
        (Features.Routes.DisposalPage(), "Disposal", "bi-trash", "Components", null),
        (Features.Routes.EventsPage(), "Events", "bi-mouse", "Components", null),
        (Features.Routes.TablePage(), "Data table", "bi-table", "Components", null),
        (Features.Routes.OrdersPage(), "Master-detail", "bi-list-nested", "Components", null),
        (Features.Routes.ScopedCssPage(), "Scoped CSS", "bi-palette", "Styling", null),
        (Features.Routes.AssetLoadingPage(), "Asset loading", "bi-link-45deg", "Styling", null),
        (Features.Routes.HttpPage(), "HttpClient + DI", "bi-cloud-arrow-down", "Data", null),
        (Features.Routes.UploadPage(), "File upload", "bi-upload", "Files", null),
        (Features.Routes.DownloadPage(), "File download", "bi-cloud-download", "Files", null),
        (Features.Routes.TodosPage(), "Todos", "bi-check2-square", "Apps", null),
        (Features.Routes.JsRuntimePage(), "IJSRuntime", "bi-braces", "Apps", null)
        // The typed browser-API wrappers used to live here as one example page each; they are now folded
        // into the Browser APIs guide as inline live demos (docs/browser-apis.md). See DemoRegistry.
    ];

    // The Bootstrap section: components from the Rask.Bootstrap package.
    private static readonly (string Path, string Label, string Icon, string Group, string? MatchPrefix)[] BootstrapLinks =
    [
        (Features.Routes.BsNavPage(), "Navbar & nav", "bi-list-nested", "Navigation", null),
        (Features.Routes.BsButtonsPage(), "Buttons & badges", "bi-hand-index-thumb", "Content", null),
        (Features.Routes.BsCardsPage(), "Cards", "bi-window", "Content", null),
        (Features.Routes.BsAlertsPage(), "Alerts", "bi-exclamation-triangle", "Content", null),
        (Features.Routes.BsIconsPage(), "Icons", "bi-emoji-smile", "Content", null),
        (Features.Routes.BsModalPage(), "Modal", "bi-window-stack", "Interactive", null),
        (Features.Routes.BsTabsPage(), "Tabs & accordion", "bi-segmented-nav", "Interactive", null),
        (Features.Routes.BsFormsPage(), "Forms", "bi-input-cursor-text", "Forms", null),
        (Features.Routes.BsUtilitiesPage(), "Utility classes", "bi-magic", "Utilities", null)
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

    protected override RenderResult Render() =>
    [
        BsNavbar(Color: BsColor.Dark, Theme: BsTheme.Dark, Sticky: true,
            Class: Bs.Join(Border.Bottom, Shadow.Sm, "app-navbar"))[
            Button(Type: "button", Class: Bs.Join("hamburger-btn", Display.None(Bp.Md)),
                OnClick: () => _drawerOpen = !_drawerOpen)[
                BsIcon(Name: _drawerOpen ? BsIconName.XLg : BsIconName.List)
            ],
            NavLink(Href: Features.Routes.HomePage(), ActiveClass: "",
                Class: Bs.Join("navbar-brand", Font.Semibold, Display.InlineFlex(), Flex.Align(BsAlign.Center),
                    Flex.Gap(2)))[
                RaskLogo.Mark(24, "brandBolt"),
                Span()["Rask"],
                BsBadge(Pill: true, Class: "rask-badge")["showcase"],
                BsBadge(Color: BsColor.Secondary, Pill: true)[$"v{RaskVersion.Current}"]
            ],
            Div(Class: Bs.Join(Display.Flex(), Flex.Align(BsAlign.Center), Flex.Gap(2), Margin.StartAuto))[
                PathDisplay(),
                A("https://github.com/pal-tamas/rask", "_blank", Class: "btn btn-outline-light btn-sm")[
                    BsIcon(Name: BsIconName.Github, Class: "me-1"), "GitHub"]
            ]
        ],
        Div(Class: Bs.Join(Display.Flex(), "app-shell"))[
            BsOffcanvas(Responsive: Bp.Md, Placement: BsPlacement.Start, Open: _drawerOpen,
                OnClose: () => _drawerOpen = false, Title: "Menu", Class: "side-nav")[
                SidebarBody()
            ],
            Main(Class: Bs.Join(Flex.Grow(1), Padding.Y(4), Padding.X(3), Padding.X(5, Bp.Md), "page-main"))[
                Div(Class: Bs.Join(Margin.XAuto, "page-main-inner"))[Outlet()]
            ]
        ]
    ];

    private RenderResult SidebarBody() => Fragment()[
        Div(Class: "side-nav-search")[
            BsInput(Value: _filter, OnChange: v => _filter = v ?? "", Size: BsSize.Sm,
                Placeholder: "Filter guides & examples…", Class: "side-nav-filter")
        ],
        Fragment()[BuildSections()]
    ];

    // Guides-first: the narrative guides are the primary spine (top of the sidebar, groups expanded by
    // default via OpenGuideGroups), followed by the interactive Examples (the framework/core showcase
    // plus any host-contributed entries, e.g. the WASM PWA examples) and the Bootstrap-component
    // showcase — both demoted below the guides and collapsed until visited.
    private IEnumerable<(string Section, IEnumerable<(string Path, string Label, string Icon, string Group, string? MatchPrefix)> Links)> Sections()
    {
        yield return ("Guides", GuidesNav());
        yield return ("Examples",
            Links.Concat(extraNav.Select(e => (e.Path, e.Label, e.Icon, e.Group, e.MatchPrefix))));
        yield return ("Bootstrap", BootstrapLinks);
    }

    // The Guides section mirrors the GuideCatalog (docs/*.md rendered on-site), led by the index.
    private static IEnumerable<(string Path, string Label, string Icon, string Group, string? MatchPrefix)> GuidesNav()
    {
        yield return (Features.Routes.GuidesIndexPage(), "All guides", "bi-book", "Overview", null);
        foreach (var g in Features.GuideCatalog.All)
        {
            yield return (Features.Routes.GuidePage(g.Slug), g.Title, g.Icon, g.Group, null);
        }
    }

    private List<Child> BuildSections()
    {
        var children = new List<Child>();
        var filtering = _filter.Length > 0;

        foreach (var (section, links) in Sections())
        {
            var groups = new List<Child>();
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

            children.Add(Div(Class: "side-nav-section")[section]);
            children.AddRange(groups);
        }

        if (children.Count == 0)
        {
            children.Add(Div(Class: "side-nav-empty text-secondary small")["Nothing matches that filter."]);
        }

        return children;
    }

    private Child GroupBlock(
        string key, string group, bool open,
        IReadOnlyList<(string Path, string Label, string Icon, string Group, string? MatchPrefix)> items) =>
        Div(Class: "nav-group", Key: key)[
            Button(Type: "button", Class: Bs.Join("nav-group-toggle", open ? "open" : null),
                OnClick: () => ToggleGroup(key))[
                I(Class: open ? "bi bi-chevron-down nav-group-chevron" : "bi bi-chevron-right nav-group-chevron"),
                Span(Class: "nav-group-label")[group]
            ],
            BsCollapse(Open: open)[
                BsNav(Vertical: true, Class: "nav-group-items")[
                    items.Select(i =>
                    {
                        RouteUrl? match = null;
                        if (i.MatchPrefix is { } mp)
                        {
                            match = mp;
                        }

                        return (Child)BsNavItem(Href: i.Path, Match: match,
                            ActiveMatch: i.MatchPrefix is null ? null : NavLinkMatch.Prefix,
                            Key: i.Path, Class: "side-nav-link")[
                            I(Class: $"bi {i.Icon} me-2"),
                            Span()[i.Label]
                        ];
                    })
                ]
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
    private static IEnumerable<(string Group, List<(string Path, string Label, string Icon, string Group, string? MatchPrefix)> Items)>
        GroupConsecutive(IEnumerable<(string Path, string Label, string Icon, string Group, string? MatchPrefix)> links)
    {
        string? current = null;
        List<(string Path, string Label, string Icon, string Group, string? MatchPrefix)>? bucket = null;

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
