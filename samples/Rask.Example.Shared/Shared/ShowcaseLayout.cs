using Microsoft.JSInterop;
using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Html.Components;

namespace Rask.Example.Shared;

[Route("/")]
public sealed partial class ShowcaseLayout(RouteState route, IEnumerable<ShowcaseNavEntry> extraNav, IJSRuntime js)
    : Component
{
    private static readonly IReadOnlyDictionary<string, string?> ThemeToggleAria =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = "Toggle light / dark theme" };

    // Flip the color theme via the scoped module (sets data-theme + data-bs-theme on <html> and persists
    // the choice). Client-only; a torn-down transport just no-ops.
    private async Task ToggleThemeAsync()
    {
        try
        {
            await js.InvokeVoidAsync("Rask.ShowcaseLayout.toggleTheme");
        }
        catch (JSDisconnectedException)
        {
            // The circuit went away — nothing to toggle.
        }
    }

    // MatchPrefix: optional section prefix for parameterised links. When set, the
    // sidebar entry stays highlighted for any URL under that prefix (e.g. switching
    // /realtime/BTC ↔ /realtime/ETH keeps "Live ticker" active). Null means
    // exact-match only.
    private static readonly (string Path, string Label, string Icon, string Group, string? MatchPrefix)[] Links =
    [
        // Paths are type-safe, generator-emitted route URLs (Features.Routes.*) — RouteUrl converts
        // implicitly to the string Path slot, so a renamed/removed [Route] is a compile error here, not a
        // dead link. MatchPrefix stays a bare string (it is a URL prefix, not a whole route).
        (Features.Routes.TodosPage(), "Todos", "bi-check2-square", "Apps", null)
        // Many example pages are now folded into their guides as inline live demos: HttpClient+DI /
        // upload / download → HTTP & files (docs/http-and-files.md); typed browser-API wrappers → Browser
        // APIs (docs/browser-apis.md); Events + Toast messages → Composition (docs/composition.md); the
        // BsToast element → Bootstrap (docs/bootstrap.md); User & auth → Authentication (docs/authentication.md); User
        // components → Getting started (docs/getting-started.md); Live ticker → Lifecycle
        // (docs/lifecycle.md); Master-detail → Composition (docs/composition.md keyed lists). The Data
        // table stays a working [QueryParam] example at /table (unlisted) — its source is shown in
        // docs/routing.md's query-param section. See DemoRegistry.
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
        // Under the native mobile shell the real UINavigationBar/MaterialToolbar takes over, so drop the web
        // navbar there. IsNative is false on Server/WASM, so the web showcase is unchanged.
        IsNative ? null : BsNavbar
            .Color(BsColor.Dark)
            .Theme(BsTheme.Dark)
            .Sticky(true)
            .Class(Bs.Join(Border.Bottom, Shadow.Sm, "app-navbar"))[
            Button
                .Type("button")
                .Class(Bs.Join("hamburger-btn", Display.None(Bp.Md)))
                .OnClick(() => _drawerOpen = !_drawerOpen)[
                BsIcon.Name(_drawerOpen ? BsIconName.XLg : BsIconName.List)
            ],
            NavLink
                .Href(Features.Routes.GuidesIndexPage())
                .ActiveClass("")
                .Class(Bs.Join("navbar-brand", Font.Semibold, Display.InlineFlex(), Flex.Align(BsAlign.Center),
                    Flex.Gap(2)))[
                RaskLogo.Size(24).GradientId("brandBolt"),
                Span["Rask"],
                BsBadge.Pill(true).Class("rask-badge")["showcase"],
                BsBadge.Color(BsColor.Secondary).Pill(true)[$"v{RaskVersion.Current}"]
            ],
            Div.Class(Bs.Join(Display.Flex(), Flex.Align(BsAlign.Center), Flex.Gap(2), Margin.StartAuto))[
                PathDisplay,
                // The live playground is a separate WASM sub-app (Roslyn compiles Rask C# in the browser),
                // deployed only to GitHub Pages alongside this showcase. This layout is shared by the
                // Server, WASM and native showcases (and runs locally), none of which serve a /playground
                // route — so link to the one place it actually lives (absolute), opened in a new tab.
                BsLink
                    .Href("https://pal-tamas.github.io/rask/playground/")
                    .Target("_blank")
                    .Rel("noopener")
                    .Color(BsColor.Primary)
                    .Size(BsSize.Sm)[
                    BsIcon.Name(BsIconName.PlayFill).Class("me-1"), "Playground"],
                BsLink
                    .Href("https://github.com/pal-tamas/rask")
                    .Target("_blank")
                    .Rel("noopener")
                    .Color(BsColor.Light)
                    .Outline(true)
                    .Size(BsSize.Sm)[
                    BsIcon.Name(BsIconName.Github).Class("me-1"), "GitHub"],
                // Light/dark theme toggle — flips data-theme + data-bs-theme via the scoped module.
                BsButton
                    .Color(BsColor.Light)
                    .Outline(true)
                    .Size(BsSize.Sm)
                    .OnClickAsync(ToggleThemeAsync)
                    .Aria(ThemeToggleAria)[
                    BsIcon.Name(BsIconName.CircleHalf)]
            ]
        ],
        Div.Class(Bs.Join(Display.Flex(), "app-shell"))[
            BsOffcanvas
                .Responsive(Bp.Md)
                .Placement(BsPlacement.Start)
                .Open(_drawerOpen)
                .OnClose(() => _drawerOpen = false)
                .Title("Menu")
                .Class("side-nav")[
                SidebarBody()
            ],
            Main.Class(Bs.Join(Flex.Grow(1), Padding.Y(4), Padding.X(3), Padding.X(5, Bp.Md), "page-main"))[
                Div.Class(Bs.Join(Margin.XAuto, "page-main-inner"))[Outlet]
            ]
        ]
    ];

    // The sidebar body is a non-scrolling flex column: a pinned filter header (.side-nav-search) over a
    // single scrolling list (.side-nav-scroll). The filter is a real flex header rather than a
    // position:sticky child because sticky-in-flexbox is unreliable in Safari (the filter would scroll
    // away with the list), and this keeps it rock-solid across browsers with a clean hairline divider.
    private Component SidebarBody() => [
        Div.Class("side-nav-search")[
            BsInput
                .Value(_filter)
                .OnChange(v => _filter = v ?? "")
                .Size(BsSize.Sm)
                .Placeholder("Filter guides & examples…")
                .Class("side-nav-filter")
        ],
        Div.Class("side-nav-scroll")[BuildSections()]
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
            children.Add(Div.Class("side-nav-empty text-secondary small")["Nothing matches that filter."]);
        }

        return children;
    }

    private Component GroupBlock(
        string key, string group, bool open,
        IReadOnlyList<(string Path, string Label, string Icon, string Group, string? MatchPrefix)> items) =>
        Div.Class("nav-group").Key(key)[
            Button
                .Type("button")
                .Class(Bs.Join("nav-group-toggle", open ? "open" : null))
                .OnClick(() => ToggleGroup(key))[
                I.Class(open ? "bi bi-chevron-down nav-group-chevron" : "bi bi-chevron-right nav-group-chevron"),
                Span.Class("nav-group-label")[group]
            ],
            BsCollapse.Open(open)[
                BsNav.Vertical(true).Class("nav-group-items")[
                    items.Select(i =>
                    {
                        RouteUrl? match = null;
                        if (i.MatchPrefix is { } mp)
                        {
                            match = mp;
                        }

                        return (Component)BsNavItem
                            .Href(i.Path)
                            .Match(match)
                            .ActiveMatch(i.MatchPrefix is null ? null : NavLinkMatch.Prefix)
                            .Key(i.Path)
                            .Class("side-nav-link")[
                            I.Class($"bi {i.Icon} me-2"),
                            Span[i.Label]
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
