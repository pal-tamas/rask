using Rask.Core.Routing;

namespace Rask.Example.Shared;

[Route("")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class HomePage(Navigator nav) : Component
{
    protected override string? Css => """
                                      .hero-card {
                                          background: linear-gradient(135deg, var(--rask-accent-soft) 0%, #ffffff 100%);
                                          border: 1px solid var(--bs-border-color);
                                      }
                                      .feature-card {
                                          transition: transform 140ms ease, box-shadow 140ms ease;
                                      }
                                      .feature-card:hover {
                                          transform: translateY(-2px);
                                      }
                                      .feature-icon {
                                          width: 2.4rem;
                                          height: 2.4rem;
                                          border-radius: 0.6rem;
                                          display: inline-flex;
                                          align-items: center;
                                          justify-content: center;
                                          background: var(--rask-accent-soft);
                                          color: var(--rask-accent);
                                          font-size: 1.2rem;
                                      }
                                      """;

    protected override Component Render() =>
        Fragment(
            Div(Class: "p-4 p-md-5 mb-4 rounded-3 hero-card", Children:
            [
                Div(Class: "container-fluid py-3", Children:
                [
                    H1(Class: "display-5 fw-bold mb-3", Children:
                    [
                        "The Rask framework, ",
                        Span(Class: "text-accent", Children: ["one page at a time."])
                    ]),
                    P(Class: "fs-5 col-md-10 text-secondary mb-4", Children:
                    [
                        "A small C# DSL for HTML — components, routing, lifecycle, scoped CSS, ",
                        "and a browser-WASM client. This site is itself a Rask WASM app; ",
                        "every example below renders live in your browser."
                    ]),
                    Div(Class: "d-flex flex-wrap gap-2", Children:
                    [
                        Button(
                            Class: "btn btn-primary btn-lg",
                            OnClick: () => nav.Navigate("/tags"),
                            Children: [I(Class: "bi bi-arrow-right me-2"), "Start with Tags"]),
                        A("https://github.com/pal-tamas/rask",
                            "_blank",
                            Class: "btn btn-outline-secondary btn-lg",
                            Children: [I(Class: "bi bi-github me-2"), "Source on GitHub"])
                    ])
                ])
            ]),
            Components.CodeSample(
                """
                using static Rask.Core.Tags;

                Fragment(
                    Doctype(),
                    Html("en", Children: [
                        Head(Children: [Title(Children: ["Hi"])]),
                        Body(Children: [
                            H1(Children: ["Hello, world!"]),
                            P(Children: ["A page rendered with Rask."])
                        ])
                    ])
                );
                """,
                "The minimal page",
                Notes:
                "Static factories from Rask.Core.Tags build a tree. Strings convert implicitly to Child. Component.ToHtml() produces the final HTML.",
                Result: Fragment(
                    H1(Class: "h3 mb-2", Children: ["Hello, world!"]),
                    P(Class: "text-secondary mb-0", Children: ["A page rendered with Rask."])
                )),
            H2(Class: "h4 mt-5 mb-3", Children: ["What's covered"]),
            Div(Class: "row g-3 mb-4", Children:
            [
                FeatureCard("bi-code-slash", "DSL",
                    "Every HTML element as a strongly-typed factory. Universal Id/Class/Style/Data on every tag.",
                    "/tags"),
                FeatureCard("bi-boxes", "Components",
                    "Sealed classes with Render(). Source-generated factories with required and optional params, DI through the constructor.",
                    "/components"),
                FeatureCard("bi-signpost-2", "Routing",
                    "[Route], [ParentRoute], [RouteParam], [QueryParam]. Nested layouts via Outlet().", "/routing"),
                FeatureCard("bi-arrow-repeat", "Lifecycle",
                    "OnInitialized, OnParametersSet, OnAfterRender — sync and async, with auto re-render after each await.",
                    "/lifecycle"),
                FeatureCard("bi-palette", "Scoped CSS",
                    "Co-locate styles on the component. Rask hashes the type name and rewrites selectors so two components can share .box.",
                    "/scoped-css"),
                FeatureCard("bi-cloud-arrow-down", "HttpClient + DI",
                    "Standard ServiceCollection. Inject HttpClient via the primary constructor and fetch in OnInitializedAsync.",
                    "/http")
            ]),
            Div(Class: "alert alert-info d-flex align-items-start", Children:
            [
                I(Class: "bi bi-info-circle-fill me-3 fs-4"),
                Div(Children:
                [
                    Strong(Children: ["Tip:"]),
                    " every page on the left has both a runnable demo and the C# source that produced it — copy/paste them into a fresh Rask project to follow along."
                ])
            ])
        );

    private Component FeatureCard(string icon, string title, string body, string path) =>
        Div(Class: "col-md-6 col-lg-4", Children:
        [
            Div(Class: "card h-100 border-0 shadow-sm feature-card", Children:
            [
                Div(Class: "card-body p-4", Children:
                [
                    Div(Class: "feature-icon mb-3", Children: [I(Class: $"bi {icon}")]),
                    H3(Class: "h6 fw-semibold mb-2", Children: [title]),
                    P(Class: "text-secondary small mb-3", Children: [body]),
                    Button(
                        Class: "btn btn-sm btn-link p-0 text-decoration-none",
                        OnClick: () => nav.Navigate(path),
                        Children: ["Explore ", I(Class: "bi bi-arrow-right ms-1")])
                ])
            ])
        ]);
}
