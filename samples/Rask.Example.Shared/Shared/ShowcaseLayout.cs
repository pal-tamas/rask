using Rask.Core.Routing;

namespace Rask.Example.Shared;

[Route("/")]
public sealed class ShowcaseLayout(Navigator nav, RouteState route, IEnumerable<ShowcaseNavEntry> extraNav) : Component
{
    // MatchPrefix: optional section prefix for parameterised links. When set, the
    // sidebar entry stays highlighted for any URL under that prefix (e.g. switching
    // /realtime/BTC ↔ /realtime/ETH keeps "Live ticker" active). Null means
    // exact-match only.
    private static readonly (string Path, string Label, string Icon, string Group, string? MatchPrefix)[] Links =
    [
        ("/", "Welcome", "bi-house", "Start", null),
        ("/tags", "Tag factories", "bi-code-slash", "DSL", null),
        ("/primitives", "Primitives", "bi-asterisk", "DSL", null),
        ("/props", "Universal props", "bi-gear", "DSL", null),
        ("/components", "User components", "bi-boxes", "Components", null),
        ("/routing", "Routing", "bi-signpost-2", "Components", null),
        ("/users/42", "Route + query params", "bi-link-45deg", "Components", "/users"),
        ("/navigator", "Navigator", "bi-compass", "Components", null),
        ("/lifecycle", "Lifecycle", "bi-arrow-repeat", "Components", null),
        ("/context", "Context", "bi-diagram-2", "Components", null),
        ("/callback", "Callback", "bi-arrow-up-right-circle", "Components", null),
        ("/element-ref", "Element refs", "bi-bullseye", "Components", null),
        ("/user", "User & auth", "bi-person-lock", "Components", null),
        ("/realtime/BTC", "Live ticker", "bi-graph-up-arrow", "Components", "/realtime"),
        ("/background", "Background service", "bi-broadcast", "Components", null),
        ("/cancellation", "Cancellation", "bi-x-circle", "Components", null),
        ("/disposal", "Disposal", "bi-trash", "Components", null),
        ("/events", "Events", "bi-mouse", "Components", null),
        ("/virtualize", "Virtualize", "bi-list-ol", "Components", null),
        ("/table", "Data table", "bi-table", "Components", null),
        ("/master-detail", "Master-detail", "bi-list-nested", "Components", null),
        ("/keyed-lists", "Keyed lists", "bi-key", "Components", null),
        ("/drag-drop", "Drag & drop", "bi-arrows-move", "Components", null),
        ("/boom", "Error boundary", "bi-shield-exclamation", "Components", null),
        ("/binding", "Two-way binding", "bi-arrow-left-right", "Forms", null),
        ("/validation", "Validation", "bi-shield-check", "Forms", null),
        ("/floating-labels", "Floating labels", "bi-input-cursor-text", "Forms", null),
        ("/nested-forms", "Complex models", "bi-diagram-3", "Forms", null),
        ("/form-groups", "Radio & checkbox", "bi-ui-radios", "Forms", null),
        ("/multiselect", "Multi-select", "bi-ui-checks", "Forms", null),
        ("/svg", "SVG", "bi-vector-pen", "DSL", null),
        ("/scoped-css", "Scoped CSS", "bi-palette", "Styling", null),
        ("/asset-loading", "Asset loading", "bi-link-45deg", "Styling", null),
        ("/http", "HttpClient + DI", "bi-cloud-arrow-down", "Data", null),
        ("/upload", "File upload", "bi-upload", "Files", null),
        ("/download", "File download", "bi-cloud-download", "Files", null),
        ("/todos", "Todos", "bi-check2-square", "Apps", null),
        ("/jsruntime", "IJSRuntime", "bi-braces", "Apps", null),
        // Type-safe, generator-emitted route URLs (Features.Routes.*) — RouteUrl converts implicitly to
        // the string Path slot, so a renamed/removed [Route] is a compile error here, not a dead link.
        (Features.Routes.StoragePage(), "Storage", "bi-hdd", "Browser APIs", null),
        (Features.Routes.CookiesPage(), "Cookies", "bi-database", "Browser APIs", null),
        (Features.Routes.ClipboardPage(), "Clipboard", "bi-clipboard", "Browser APIs", null),
        (Features.Routes.GeolocationPage(), "Geolocation", "bi-geo-alt", "Browser APIs", null),
        (Features.Routes.PermissionsPage(), "Permissions", "bi-shield-lock", "Browser APIs", null),
        (Features.Routes.VibrationPage(), "Vibration", "bi-phone-vibrate", "Browser APIs", null),
        (Features.Routes.PageVisibilityPage(), "Page visibility", "bi-eye", "Browser APIs", null),
        (Features.Routes.NavigatorInfoPage(), "Browser info", "bi-info-circle", "Browser APIs", null)
    ];

    private bool _drawerOpen;

    // Subscribe to RouteState.Changed so the sidebar's active-class computation refreshes
    // on every nav (including browser back/forward) without resorting to BypassRenderCache.
    // That keeps the layout's normal render-cache behaviour in place — it doesn't re-run on
    // every form-input keystroke from a child page, only when the route actually changes.
    protected override void OnMount() => route.Changed += StateHasChanged;

    protected override void OnUnmount() => route.Changed -= StateHasChanged;

    protected override RenderResult Render() =>
    [
        Nav(Class: "navbar navbar-dark bg-dark border-bottom shadow-sm sticky-top")[
            Div(Class: "container-fluid")[
                Button(
                    Class: "hamburger-btn",
                    Type: "button",
                    OnClick: () => _drawerOpen = !_drawerOpen)[
                    I(Class: _drawerOpen ? "bi bi-x-lg" : "bi bi-list")
                ],
                Button(
                    Class: "navbar-brand fw-semibold border-0 bg-transparent d-inline-flex align-items-center gap-2",
                    OnClick: () =>
                    {
                        _drawerOpen = false;
                        nav.NavigateTo("/");
                    })[
                    RaskLogo.Mark(24, "brandBolt"),
                    Span()["Rask"],
                    Span(Class: "badge rounded-pill rask-badge")["showcase"],
                    Span(Class: "badge rounded-pill text-bg-secondary")[$"v{RaskVersion.Current}"]
                ],
                Div(Class: "d-flex align-items-center gap-2 ms-auto")[
                    PathDisplay(),
                    A("https://github.com/pal-tamas/rask",
                        "_blank",
                        Class: "btn btn-outline-light btn-sm")[I(Class: "bi bi-github me-1"), "GitHub"]
                ]
            ]
        ],
        Button(
            Class: _drawerOpen ? "nav-backdrop drawer-open" : "nav-backdrop",
            Type: "button",
            OnClick: () => _drawerOpen = false),
        Div(Class: "container-fluid")[
            Div(Class: "row")[
                Aside(Class: _drawerOpen
                    ? "col-12 col-md-4 col-lg-3 col-xl-2 bg-white border-end side-nav drawer-open"
                    : "col-12 col-md-4 col-lg-3 col-xl-2 bg-white border-end side-nav")[
                    Div(Class: "position-sticky pt-3 pb-4 px-2", Style: "top: var(--nav-h);")[BuildGroups()]
                ],
                Main(Class: "col-12 col-md-8 col-lg-9 col-xl-10 py-4 px-md-5")[
                    Div(Class: "mx-auto", Style: "max-width: 1280px;")[Outlet()]
                ]
            ]
        ]
    ];

    private List<Child> BuildGroups()
    {
        var children = new List<Child>();
        string? currentGroup = null;
        // Static showcase links, then any host-contributed entries (e.g. the WASM PWA example).
        var all = Links.Concat(
            extraNav.Select(e => (e.Path, e.Label, e.Icon, e.Group, e.MatchPrefix)));
        foreach (var (path, label, icon, group, matchPrefix) in all)
        {
            if (group != currentGroup)
            {
                children.Add(H6(Class: "text-uppercase text-secondary small fw-bold mt-3 mb-2 px-2")[group]);
                currentGroup = group;
            }

            var active = IsActive(path, matchPrefix);
            children.Add(Button(
                Class: active
                    ? "nav-item-btn nav-item-btn-active"
                    : "nav-item-btn",
                OnClick: () =>
                {
                    _drawerOpen = false;
                    nav.NavigateTo(path);
                })[
                I(Class: $"bi {icon} me-2"),
                Span()[label]
            ]);
        }

        return children;
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
