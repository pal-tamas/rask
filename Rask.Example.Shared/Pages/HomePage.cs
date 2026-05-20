using Rask.Core.Routing;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class HomePage(Navigator nav) : Component
{
    protected override Component? Head => Title()["Welcome — Rask"];

    protected override Component Render() =>
        Fragment()[
            Div(Class: "p-4 p-md-5 mb-4 rounded-3 hero-card")[
                Div(Class: "container-fluid py-3")[
                    H1(Class: "display-5 fw-bold mb-3")[
                        "The Rask framework, ",
                        Span(Class: "text-accent")["one page at a time."]
                    ],
                    P(Class: "fs-5 col-md-10 text-secondary mb-4")[
                        "A small C# DSL for HTML — components, routing, lifecycle, scoped CSS, ",
                        "and a browser-WASM client. This site is itself a Rask WASM app; ",
                        "every example below renders live in your browser."
                    ],
                    Div(Class: "d-flex flex-wrap gap-2")[
                        Button(
                            Class: "btn btn-primary btn-lg",
                            OnClick: () => nav.Navigate("/tags"))[I(Class: "bi bi-arrow-right me-2"),
                            "Start with Tags"],
                        A("https://github.com/pal-tamas/rask",
                            "_blank",
                            Class: "btn btn-outline-secondary btn-lg")[I(Class: "bi bi-github me-2"),
                            "Source on GitHub"]
                    ]
                ]
            ],
            CodeSample(
                """
                Fragment(
                    Doctype(),
                    Html("en")[
                        Head()[Title()["Hi"]],
                        Body()[
                            H1()["Hello, world!"],
                            P()["A page rendered with Rask."]
                        ]
                    ]
                );
                """,
                "The minimal page",
                Notes:
                "Generator-emitted factories build a tree. Strings convert implicitly to Child. Component.ToHtml() produces the final HTML.",
                Result: Fragment()[
                    H1(Class: "h3 mb-2")["Hello, world!"],
                    P(Class: "text-secondary mb-0")["A page rendered with Rask."]
                ]),
            H2(Class: "h4 mt-5 mb-3")["What's covered"],
            Div(Class: "row g-3 mb-4")[
                FeatureCard("bi-code-slash", "DSL",
                    "Every HTML element as a strongly-typed factory. Universal Id/Class/Style/Data on every tag.",
                    "/tags"),
                FeatureCard("bi-boxes", "Components",
                    "Sealed classes with Render(). Source-generated factories with required and optional params, DI through the constructor.",
                    "/components"),
                FeatureCard("bi-signpost-2", "Routing",
                    "[Route], [ParentRoute], [RouteParam], [QueryParam]. Nested layouts via Outlet().", "/routing"),
                FeatureCard("bi-arrow-repeat", "Lifecycle",
                    "OnMount, OnPropsChanged, OnRendered — sync and async, with auto re-render after each await.",
                    "/lifecycle"),
                FeatureCard("bi-x-circle", "Cancellation",
                    "Every Component exposes a protected CancellationToken that fires on unmount. Pass it into HttpClient or Task.Delay and in-flight work aborts cleanly.",
                    "/cancellation"),
                FeatureCard("bi-trash", "Disposal",
                    "Components that implement IDisposable / IAsyncDisposable get Dispose called when removed from the tree. Use it for timers, subscriptions, or DI handles.",
                    "/disposal"),
                FeatureCard("bi-palette", "Scoped CSS",
                    "Co-locate styles on the component. Rask hashes the type name and rewrites selectors so two components can share .box.",
                    "/scoped-css"),
                FeatureCard("bi-cloud-arrow-down", "HttpClient + DI",
                    "Standard ServiceCollection. Inject HttpClient via the primary constructor and fetch in OnMountAsync.",
                    "/http")
            ],
            Div(Class: "alert alert-info d-flex align-items-start")[
                I(Class: "bi bi-info-circle-fill me-3 fs-4"),
                Div()[
                    Strong()["Tip:"],
                    " every page on the left has both a runnable demo and the C# source that produced it — copy/paste them into a fresh Rask project to follow along."
                ]
            ]
        ];

    private Component FeatureCard(string icon, string title, string body, string path) =>
        Div(Class: "col-md-6 col-lg-4")[
            Div(Class: "card h-100 border-0 shadow-sm feature-card")[
                Div(Class: "card-body p-4")[
                    Div(Class: "feature-icon mb-3")[I(Class: $"bi {icon}")],
                    H3(Class: "h6 fw-semibold mb-2")[title],
                    P(Class: "text-secondary small mb-3")[body],
                    Button(
                        Class: "btn btn-sm btn-link p-0 text-decoration-none",
                        OnClick: () => nav.Navigate(path))["Explore ", I(Class: "bi bi-arrow-right ms-1")]
                ]
            ]
        ];
}
