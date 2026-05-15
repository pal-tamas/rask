using Rask.Core.Routing;

namespace Rask.Example.Shared.Layout;

[Route("/")]
public sealed class ShowcaseLayout(Navigator nav, RouteState route) : Component
{
    private const string LayoutCss = """
                                     .side-nav {
                                         min-height: calc(100vh - 56px);
                                     }
                                     .nav-item-btn {
                                         display: flex;
                                         align-items: center;
                                         width: 100%;
                                         min-height: 44px;
                                         padding: 0.7rem 0.85rem;
                                         border: 1px solid transparent;
                                         border-radius: 0.5rem;
                                         background: transparent;
                                         color: #333;
                                         font-size: 0.92rem;
                                         text-align: left;
                                         margin-bottom: 0.15rem;
                                         transition: background-color 120ms ease, color 120ms ease;
                                     }
                                     .nav-item-btn:hover {
                                         background: var(--rask-accent-soft);
                                         color: var(--rask-accent-strong);
                                     }
                                     .nav-item-btn-active {
                                         background: var(--rask-accent-soft);
                                         color: var(--rask-accent);
                                         font-weight: 600;
                                     }
                                     .nav-item-btn .bi {
                                         font-size: 1rem;
                                         opacity: 0.85;
                                     }
                                     .navbar.sticky-top {
                                         background: rgba(28, 30, 36, 0.78) !important;
                                         backdrop-filter: saturate(180%) blur(14px);
                                         -webkit-backdrop-filter: saturate(180%) blur(14px);
                                         padding-top: calc(0.5rem + env(safe-area-inset-top));
                                     }
                                     .hamburger-btn {
                                         display: none;
                                         min-width: 44px;
                                         min-height: 44px;
                                         align-items: center;
                                         justify-content: center;
                                         border: 0;
                                         background: transparent;
                                         color: #fff;
                                         border-radius: 0.5rem;
                                         margin-right: 0.25rem;
                                         padding: 0;
                                     }
                                     .hamburger-btn:hover, .hamburger-btn:focus {
                                         background: rgba(255, 255, 255, 0.08);
                                         outline: none;
                                     }
                                     .hamburger-btn .bi {
                                         font-size: 1.4rem;
                                         line-height: 1;
                                     }
                                     .nav-backdrop {
                                         display: none;
                                         border: 0;
                                         padding: 0;
                                         margin: 0;
                                     }
                                     main {
                                         padding-bottom: calc(2rem + env(safe-area-inset-bottom));
                                     }
                                     @media (max-width: 767.98px) {
                                         .hamburger-btn { display: inline-flex; }
                                         .side-nav {
                                             position: fixed;
                                             top: 0;
                                             left: 0;
                                             bottom: 0;
                                             width: min(82vw, 320px);
                                             max-width: 320px;
                                             z-index: 1045;
                                             background: #fff;
                                             transform: translateX(-100%);
                                             transition: transform 220ms cubic-bezier(0.22, 1, 0.36, 1);
                                             will-change: transform;
                                             padding-top: calc(56px + env(safe-area-inset-top));
                                             padding-bottom: env(safe-area-inset-bottom);
                                             padding-left: env(safe-area-inset-left);
                                             overflow-y: auto;
                                             -webkit-overflow-scrolling: touch;
                                             overscroll-behavior: contain;
                                             border-right: 1px solid var(--bs-border-color);
                                             min-height: auto;
                                         }
                                         .side-nav.drawer-open {
                                             transform: translateX(0);
                                             box-shadow: 0 10px 30px rgba(0, 0, 0, 0.18);
                                         }
                                         .side-nav .position-sticky {
                                             position: static !important;
                                             top: auto !important;
                                         }
                                         .nav-backdrop {
                                             display: block;
                                             position: fixed;
                                             inset: 0;
                                             background: rgba(0, 0, 0, 0.4);
                                             z-index: 1040;
                                             opacity: 0;
                                             pointer-events: none;
                                             transition: opacity 200ms ease;
                                         }
                                         .nav-backdrop.drawer-open {
                                             opacity: 1;
                                             pointer-events: auto;
                                         }
                                     }
                                     """;

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

    protected override string? Css => LayoutCss;

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
            ],
            _drawerOpen
                ? (Component)Style()[Raw("body { overflow: hidden; height: 100vh; }")]
                : Fragment()
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
