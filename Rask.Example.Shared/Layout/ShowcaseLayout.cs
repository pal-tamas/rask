using Rask.Core.Routing;

namespace Rask.Example.Shared;

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
                                         padding: 0.42rem 0.7rem;
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
                                     @media (max-width: 767.98px) {
                                         .side-nav {
                                             min-height: auto;
                                             border-right: none !important;
                                             border-bottom: 1px solid var(--bs-border-color);
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
        ("/events", "Events", "bi-mouse", "Components"),
        ("/binding", "Two-way binding", "bi-arrow-left-right", "Forms"),
        ("/validation", "Validation", "bi-shield-check", "Forms"),
        ("/scoped-css", "Scoped CSS", "bi-palette", "Styling"),
        ("/http", "HttpClient + DI", "bi-cloud-arrow-down", "Data")
    ];

    protected override string? Css => LayoutCss;

    protected override Component Render() =>
        Fragment(
            Nav(Class: "navbar navbar-dark bg-dark border-bottom shadow-sm sticky-top", Children:
            [
                Div(Class: "container-fluid", Children:
                [
                    Button(
                        Class: "navbar-brand fw-semibold border-0 bg-transparent",
                        OnClick: () => nav.Navigate("/"),
                        Children:
                        [
                            "Rask ",
                            Span(Class: "badge rounded-pill rask-badge ms-1", Children: ["showcase"])
                        ]),
                    Div(Class: "d-flex align-items-center gap-2", Children:
                    [
                        Span(Class: "text-secondary small d-none d-md-inline", Children:
                        [
                            "path: ",
                            Code(Class: "text-info", Children: [route.Path])
                        ]),
                        A("https://github.com/pal-tamas/rask",
                            "_blank",
                            Class: "btn btn-outline-light btn-sm",
                            Children: [I(Class: "bi bi-github me-1"), "GitHub"])
                    ])
                ])
            ]),
            Div(Class: "container-fluid", Children:
            [
                Div(Class: "row", Children:
                [
                    Aside(Class: "col-12 col-md-4 col-lg-3 col-xl-2 bg-white border-end side-nav", Children:
                    [
                        Div(Class: "position-sticky pt-3 pb-4 px-2", Style: "top: 56px;", Children: BuildGroups())
                    ]),
                    Main(Class: "col-12 col-md-8 col-lg-9 col-xl-10 py-4 px-md-5", Children:
                    [
                        Div(Class: "mx-auto", Style: "max-width: 920px;", Children: [Outlet()])
                    ])
                ])
            ])
        );

    private List<Child> BuildGroups()
    {
        var children = new List<Child>();
        string? currentGroup = null;
        foreach (var (path, label, icon, group) in Links)
        {
            if (group != currentGroup)
            {
                children.Add(H6(Class: "text-uppercase text-secondary small fw-bold mt-3 mb-2 px-2",
                    Children: [group]));
                currentGroup = group;
            }

            var active = IsActive(path);
            children.Add(Button(
                Class: active
                    ? "nav-item-btn nav-item-btn-active"
                    : "nav-item-btn",
                OnClick: () => nav.Navigate(path),
                Children:
                [
                    I(Class: $"bi {icon} me-2"),
                    Span(Children: [label])
                ]));
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
