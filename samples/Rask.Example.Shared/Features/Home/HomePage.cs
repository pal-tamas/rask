using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class HomePage(Navigator nav) : Component
{
    // The showcase index, grouped to mirror the left-hand nav. Every runnable demo page is
    // surfaced here so the home page doubles as a feature map.
    private static readonly string[] SectionOrder =
        ["DSL", "Components", "Styling", "Data & files", "Apps"];

    private static readonly (string Section, string Icon, string Title, string Blurb, string Path)[] Features =
    [
        ("DSL", "bi-code-slash", "Tag factories", "Every HTML element, strongly typed.", "/guides/elements"),
        ("DSL", "bi-asterisk", "Primitives", "Text, Raw, Fragment, Doctype, Component.", "/guides/elements"),
        ("DSL", "bi-gear", "Universal props", "Id, Class, Style, Data, Ref on every tag.", "/guides/elements"),
        ("DSL", "bi-vector-pen", "SVG", "Typed SVG components.", "/guides/elements"),

        ("Components", "bi-boxes", "User components", "Sealed classes with generated factories.", "/guides/getting-started"),
        ("Components", "bi-bell", "Toast", "Show, stack, dismiss & auto-hide — no JS.", "/guides/bootstrap"),
        ("Components", "bi-megaphone", "Flash messages", "Rails-style transient messages via IFlash.", "/guides/composition"),
        ("Components", "bi-mouse", "Events", "The full DOM event surface, typed.", "/guides/composition"),
        ("Components", "bi-table", "Data table", "Sortable, paginated, URL-driven table.", "/guides/routing"),
        ("Components", "bi-list-nested", "Master-detail", "Collapsible rows with a nested datagrid.", "/guides/composition"),
        ("Components", "bi-graph-up-arrow", "Live ticker", "Lifecycle hooks + a zero-JS SVG chart.", "/guides/lifecycle"),
        ("Components", "bi-person-lock", "User & auth", "Gate UI on the current user.", "/guides/authentication"),

        ("Data & files", "bi-cloud-arrow-down", "HttpClient + DI", "Inject HttpClient, fetch in OnMountAsync.",
            "/guides/http-and-files"),
        ("Data & files", "bi-upload", "File upload", "A typed file picker and RaskFile metadata.",
            "/guides/http-and-files"),
        ("Data & files", "bi-cloud-download", "File download", "One-shot secure downloads.",
            "/guides/http-and-files"),

        ("Apps", "bi-check2-square", "Todos", "A small end-to-end app.", "/todos")

        // The typed browser-API wrappers are documented as inline live demos in the Browser APIs guide.
    ];

    protected override Component? Head => Title()["Welcome — Rask"];

    protected override Component? Render() =>
    [
        Div(Class: "p-4 p-md-5 mb-4 rounded-3 hero-card")[
            Div(Class: "container-fluid py-3")[
                Div(Class: "hero-logo mb-4")[RaskLogo.Mark(76, "heroBolt")],
                Div(Class: "hero-eyebrow")["C# UI framework"],
                H1(Class: "display-5 fw-bold mb-3")[
                    "The Rask framework, ",
                    Span(Class: "hero-accent")["one page at a time."]
                ],
                P(Class: "fs-5 col-md-10 hero-lead mb-4")[
                    "A small C# DSL for HTML — components, routing, lifecycle, scoped CSS, ",
                    "and a browser-WASM client. This site is itself a Rask WASM app; ",
                    "every example below renders live in your browser."
                ],
                Div(Class: "d-flex flex-wrap gap-2")[
                    BsButton(Color: BsColor.Light, Size: BsSize.Lg, Class: "fw-semibold", OnClick: () => nav.NavigateTo(Routes.GuidesIndexPage()))[
                        BsIcon(Name: BsIconName.Book, Class: "me-2"), "Read the guides"],
                    BsButton(Size: BsSize.Lg, Class: "btn-outline-light", OnClick: () => nav.NavigateTo(Routes.GuidePage("elements")))[
                        BsIcon(Name: BsIconName.ArrowRight, Class: "me-2"), "Start with the DSL"],
                    A("https://github.com/pal-tamas/rask",
                        "_blank",
                        Class: "btn btn-outline-light btn-lg")[BsIcon(Name: BsIconName.Github, Class: "me-2"),
                        "Source on GitHub"]
                ]
            ]
        ],
        // Guides-first: the narrative guides lead the landing page. Each reads like a Rails guide, with
        // runnable demos embedded inline, a Chapters index, an on-this-page rail, and prev/next nav.
        Section(Class: "mb-2")[
            H2(Class: "h4 mb-1")["Start with a guide"],
            P(Class: "text-secondary mb-4")[
                "Task-focused documentation with runnable demos embedded inline — the fastest way in."
            ],
            Div()[GuideCards.Render()]
        ],
        CodeSample(
            ["HomeWelcomeDemo.cs"],
            "The minimal page",
            Notes:
            "Generator-emitted factories build a tree. Strings convert implicitly to Component. Component.ToHtml() produces the final HTML.",
            Result: HomeWelcomeDemo()),
        H2(Class: "h4 mt-5 mb-1")["Browse the component examples"],
        P(Class: "text-secondary mb-4")[
            "Every interactive example in the sidebar has a runnable demo and the C# source that produced it."
        ],
        Div()[FeatureIndex()],
        BsAlert(Color: BsColor.Info, Class: "d-flex align-items-start")[
            BsIcon(Name: BsIconName.InfoCircleFill, Class: "me-3 fs-4"),
            Div()[
                Strong()["Tip:"],
                " copy/paste any demo and its source into a fresh Rask project to follow along."
            ]
        ]
    ];

    // Yields, per section in display order, a heading followed by a row of feature cards.
    private IEnumerable<Component> FeatureIndex()
    {
        foreach (var section in SectionOrder)
        {
            var cards = Features.Where(f => f.Section == section).ToArray();
            if (cards.Length == 0)
            {
                continue;
            }

            yield return H3(Class: "h6 fw-bold text-uppercase text-secondary mt-4 mb-3 feature-section")[section];
            yield return Div(Class: "row g-3")[
                cards.Select(f => (Component)FeatureCard(f.Icon, f.Title, f.Blurb, f.Path))
            ];
        }
    }

    private Component FeatureCard(string icon, string title, string body, string path) =>
        Div(Class: "col-md-6 col-lg-4", Key: path)[
            BsCard(Class: Bs.Join(Sizing.H(100), Border.None, Shadow.Sm, "feature-card"))[
                BsCardBody(Class: "p-4")[
                    Div(Class: "feature-icon mb-3")[I(Class: $"bi {icon}")],
                    H3(Class: "h6 fw-semibold mb-2")[title],
                    P(Class: "text-secondary small mb-3")[body],
                    BsButton(Size: BsSize.Sm, Class: "btn-link p-0 text-decoration-none", OnClick: () => nav.NavigateTo(path))["Explore ", BsIcon(Name: BsIconName.ArrowRight, Class: "ms-1")]
                ]
            ]
        ];
}
