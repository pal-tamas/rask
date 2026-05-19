using Rask.Core.Routing;

namespace Rask.Example.Shared.Layout;

[Route("/")]
public sealed class ShowcaseLayout(Navigator nav, RouteState route) : Component
{
    private static readonly (string Path, string Label, string Icon, string Group)[] Links =
    [
        ("/", "Welcome", "bi-house", "Start"),
        ("/tags", "Tag factories", "bi-code-slash", "DSL"),
        ("/primitives", "Primitives", "bi-asterisk", "DSL"),
        ("/props", "Universal props", "bi-gear", "DSL"),
        ("/components", "User components", "bi-boxes", "Components"),
        ("/routing", "Routing", "bi-signpost-2", "Components"),
        ("/users/42", "Route + query params", "bi-link-45deg", "Components"),
        ("/navigator", "Navigator", "bi-compass", "Components"),
        ("/lifecycle", "Lifecycle", "bi-arrow-repeat", "Components"),
        ("/cancellation", "Cancellation", "bi-x-circle", "Components"),
        ("/disposal", "Disposal", "bi-trash", "Components"),
        ("/events", "Events", "bi-mouse", "Components"),
        ("/virtualize", "Virtualize", "bi-list-ol", "Components"),
        ("/table", "Data table", "bi-table", "Components"),
        ("/boom", "Error boundary", "bi-shield-exclamation", "Components"),
        ("/binding", "Two-way binding", "bi-arrow-left-right", "Forms"),
        ("/validation", "Validation", "bi-shield-check", "Forms"),
        ("/nested-forms", "Complex models", "bi-diagram-3", "Forms"),
        ("/scoped-css", "Scoped CSS", "bi-palette", "Styling"),
        ("/view-transitions", "View transitions", "bi-stars", "Styling"),
        ("/http", "HttpClient + DI", "bi-cloud-arrow-down", "Data"),
        ("/upload", "File upload", "bi-upload", "Files"),
        ("/download", "File download", "bi-cloud-download", "Files")
    ];

    // Sidebar active-class is computed from route.Path inside Render(), which the framework
    // can't observe — opt out of the render cache so the active link updates on every nav.
    protected override bool BypassRenderCache => true;

    private bool _drawerOpen;

    protected override Component Render() =>
        Fragment()[
            Nav(Class: "navbar navbar-dark bg-dark border-bottom shadow-sm sticky-top")[
                Div(Class: "container-fluid")[
                    Button(
                        Class: "hamburger-btn",
                        Type: "button",
                        OnClick: () => _drawerOpen = !_drawerOpen)[
                            I(Class: _drawerOpen ? "bi bi-x-lg" : "bi bi-list")
                        ],
                    Button(
                        Class: "navbar-brand fw-semibold border-0 bg-transparent",
                        OnClick: () =>
                        {
                            _drawerOpen = false;
                            nav.Navigate("/");
                        })[
                            "Rask ",
                            Span(Class: "badge rounded-pill rask-badge ms-1")["showcase"]
                        ],
                    Div(Class: "d-flex align-items-center gap-2 ms-auto")[
                        Span(Class: "text-secondary small d-none d-md-inline")[
                            "path: ",
                            Code(Class: "text-info")[route.Path]
                        ],
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
                        Div(Class: "position-sticky pt-3 pb-4 px-2", Style: "top: 56px;")[BuildGroups()]
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
        foreach (var (path, label, icon, group) in Links)
        {
            if (group != currentGroup)
            {
                children.Add(H6(Class: "text-uppercase text-secondary small fw-bold mt-3 mb-2 px-2")[group]);
                currentGroup = group;
            }

            var active = IsActive(path);
            children.Add(Button(
                Class: active
                    ? "nav-item-btn nav-item-btn-active"
                    : "nav-item-btn",
                OnClick: () =>
                {
                    _drawerOpen = false;
                    nav.Navigate(path);
                })[
                    I(Class: $"bi {icon} me-2"),
                    Span()[label]
                ]);
        }

        return children;
    }

    private bool IsActive(string href)
    {
        if (href == "/")
        {
            return route.Path == "/" || string.IsNullOrEmpty(route.Path);
        }

        var trimmed = route.Path.TrimEnd('/');
        return string.Equals(trimmed, href, StringComparison.OrdinalIgnoreCase);
    }
}
