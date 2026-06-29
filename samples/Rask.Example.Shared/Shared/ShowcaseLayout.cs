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
        // Paths are type-safe, generator-emitted route URLs (Features.Routes.*) — RouteUrl converts
        // implicitly to the string Path slot, so a renamed/removed [Route] is a compile error here, not a
        // dead link. MatchPrefix stays a bare string (it is a URL prefix, not a whole route).
        (Features.Routes.HomePage(), "Welcome", "bi-house", "Start", null),
        (Features.Routes.TagsPage(), "Tag factories", "bi-code-slash", "DSL", null),
        (Features.Routes.PrimitivesPage(), "Primitives", "bi-asterisk", "DSL", null),
        (Features.Routes.PropsPage(), "Universal props", "bi-gear", "DSL", null),
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
        (Features.Routes.ContextPage(), "Context", "bi-diagram-2", "Components", null),
        (Features.Routes.CallbackPage(), "Callback", "bi-arrow-up-right-circle", "Components", null),
        (Features.Routes.ToastPage(), "Toast", "bi-bell", "Components", null),
        (Features.Routes.ElementRefPage(), "Element refs", "bi-bullseye", "Components", null),
        (Features.Routes.UserPage(), "User & auth", "bi-person-lock", "Components", null),
        (Features.Routes.LiveTickerPage("BTC"), "Live ticker", "bi-graph-up-arrow", "Components", "/realtime"),
        (Features.Routes.BackgroundServicePage(), "Background service", "bi-broadcast", "Components", null),
        (Features.Routes.CancellationPage(), "Cancellation", "bi-x-circle", "Components", null),
        (Features.Routes.DisposalPage(), "Disposal", "bi-trash", "Components", null),
        (Features.Routes.EventsPage(), "Events", "bi-mouse", "Components", null),
        (Features.Routes.VirtualizePage(), "Virtualize", "bi-list-ol", "Components", null),
        (Features.Routes.TablePage(), "Data table", "bi-table", "Components", null),
        (Features.Routes.OrdersPage(), "Master-detail", "bi-list-nested", "Components", null),
        (Features.Routes.KeyedListsPage(), "Keyed lists", "bi-key", "Components", null),
        (Features.Routes.DragDropPage(), "Drag & drop", "bi-arrows-move", "Components", null),
        (Features.Routes.BoomPage(), "Error boundary", "bi-shield-exclamation", "Components", null),
        (Features.Routes.BindingPage(), "Two-way binding", "bi-arrow-left-right", "Forms", null),
        (Features.Routes.FormControlsPage(), "Form controls", "bi-toggles", "Forms", null),
        (Features.Routes.ValidationPage(), "Validation", "bi-shield-check", "Forms", null),
        (Features.Routes.FloatingLabelsPage(), "Floating labels", "bi-input-cursor-text", "Forms", null),
        (Features.Routes.NestedFormPage(), "Complex models", "bi-diagram-3", "Forms", null),
        (Features.Routes.FormGroupsPage(), "Radio & checkbox", "bi-ui-radios", "Forms", null),
        (Features.Routes.MultiSelectPage(), "Multi-select", "bi-ui-checks", "Forms", null),
        (Features.Routes.SvgPage(), "SVG", "bi-vector-pen", "DSL", null),
        (Features.Routes.ScopedCssPage(), "Scoped CSS", "bi-palette", "Styling", null),
        (Features.Routes.AssetLoadingPage(), "Asset loading", "bi-link-45deg", "Styling", null),
        (Features.Routes.HttpPage(), "HttpClient + DI", "bi-cloud-arrow-down", "Data", null),
        (Features.Routes.UploadPage(), "File upload", "bi-upload", "Files", null),
        (Features.Routes.DownloadPage(), "File download", "bi-cloud-download", "Files", null),
        (Features.Routes.TodosPage(), "Todos", "bi-check2-square", "Apps", null),
        (Features.Routes.JsRuntimePage(), "IJSRuntime", "bi-braces", "Apps", null),
        (Features.Routes.StoragePage(), "Storage", "bi-hdd", "Browser APIs", null),
        (Features.Routes.CookiesPage(), "Cookies", "bi-database", "Browser APIs", null),
        (Features.Routes.ClipboardPage(), "Clipboard", "bi-clipboard", "Browser APIs", null),
        (Features.Routes.GeolocationPage(), "Geolocation", "bi-geo-alt", "Browser APIs", null),
        (Features.Routes.PermissionsPage(), "Permissions", "bi-shield-lock", "Browser APIs", null),
        (Features.Routes.VibrationPage(), "Vibration", "bi-phone-vibrate", "Browser APIs", null),
        (Features.Routes.PageVisibilityPage(), "Page visibility", "bi-eye", "Browser APIs", null),
        (Features.Routes.NavigatorInfoPage(), "Browser info", "bi-info-circle", "Browser APIs", null),
        (Features.Routes.NetworkInfoPage(), "Network info", "bi-reception-4", "Browser APIs", null),
        (Features.Routes.MediaQueryPage(), "Media queries", "bi-aspect-ratio", "Browser APIs", null),
        (Features.Routes.SpeechPage(), "Speech", "bi-megaphone", "Browser APIs", null),
        (Features.Routes.ScreenInfoPage(), "Screen info", "bi-display", "Browser APIs", null),
        (Features.Routes.StorageEstimatePage(), "Quota estimate", "bi-hdd-stack", "Browser APIs", null),
        (Features.Routes.VisualViewportPage(), "Visual viewport", "bi-aspect-ratio-fill", "Browser APIs", null),
        (Features.Routes.BroadcastChannelPage(), "Broadcast channel", "bi-broadcast-pin", "Browser APIs", null),
        (Features.Routes.IntersectionObserverPage(), "Intersection observer", "bi-binoculars", "Browser APIs", null),
        (Features.Routes.ResizeObserverPage(), "Resize observer", "bi-arrows-angle-expand", "Browser APIs", null),
        (Features.Routes.GeolocationWatchPage(), "Live location", "bi-geo", "Browser APIs", null),
        (Features.Routes.CryptoPage(), "Web Crypto", "bi-shield-shaded", "Browser APIs", null),
        (Features.Routes.PerformancePage(), "Performance", "bi-stopwatch", "Browser APIs", null)
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
