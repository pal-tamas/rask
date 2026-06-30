using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class HomePage(Navigator nav) : Component
{
    // The showcase index, grouped to mirror the left-hand nav. Every runnable demo page is
    // surfaced here so the home page doubles as a feature map.
    private static readonly string[] SectionOrder =
        ["DSL", "Components", "Forms", "Styling", "Data & files", "Apps"];

    private static readonly (string Section, string Icon, string Title, string Blurb, string Path)[] Features =
    [
        ("DSL", "bi-code-slash", "Tag factories", "Every HTML element, strongly typed.", "/tags"),
        ("DSL", "bi-asterisk", "Primitives", "Text, Raw, Fragment, Doctype, Child.", "/primitives"),
        ("DSL", "bi-gear", "Universal props", "Id, Class, Style, Data, Ref on every tag.", "/props"),
        ("DSL", "bi-vector-pen", "SVG", "Typed SVG components.", "/svg"),

        ("Components", "bi-boxes", "User components", "Sealed classes with generated factories.", "/components"),
        ("Components", "bi-signpost-2", "Routing", "Attributes, nested layouts, Outlet().", "/routing"),
        ("Components", "bi-link-45deg", "Route + query params", "Bind URL segments and query.", "/users/42"),
        ("Components", "bi-compass", "Navigator", "Programmatic navigation.", "/navigator"),
        ("Components", "bi-arrow-repeat", "Lifecycle", "Mount, props-changed, rendered hooks.", "/lifecycle"),
        ("Components", "bi-diagram-2", "Context", "Provide and consume without prop drilling.", "/context"),
        ("Components", "bi-arrow-up-right-circle", "Callbacks", "Child→parent events, auto re-render.", "/callback"),
        ("Components", "bi-bell", "Toast", "Show, stack, dismiss & auto-hide — no JS.", Routes.ToastPage()),
        ("Components", "bi-bullseye", "Element refs", "Reach the live DOM from C#.", "/element-ref"),
        ("Components", "bi-x-circle", "Cancellation", "A token that fires on unmount.", "/cancellation"),
        ("Components", "bi-trash", "Disposal", "IDisposable / IAsyncDisposable cleanup.", "/disposal"),
        ("Components", "bi-mouse", "Events", "DOM event handlers.", "/events"),
        ("Components", "bi-list-ol", "Virtualize", "Headless windowed lists.", "/virtualize"),
        ("Components", "bi-table", "Data table", "Sortable, paginated table.", "/table"),
        ("Components", "bi-list-nested", "Master-detail", "Collapsible rows with a nested datagrid.", "/master-detail"),
        ("Components", "bi-key", "Keyed lists", "Stable identity for trusted diffs.", "/keyed-lists"),
        ("Components", "bi-arrows-move", "Drag & drop", "Headless reordering primitive.", "/drag-drop"),
        ("Components", "bi-graph-up-arrow", "Live ticker", "Server-pushed live updates.", "/realtime/BTC"),
        ("Components", "bi-person-lock", "User & auth", "Gate UI on the current user.", "/user"),
        ("Components", "bi-shield-exclamation", "Error boundary", "Catch render and lifecycle errors.", "/boom"),

        ("Forms", "bi-arrow-left-right", "Two-way binding", "() => model.X bindings.", "/binding"),
        ("Forms", "bi-shield-check", "Validation", "Inline, DataAnnotations, FluentValidation.", "/validation"),
        ("Forms", "bi-input-cursor-text", "Floating labels", "Bootstrap floating labels + validation.", "/floating-labels"),
        ("Forms", "bi-diagram-3", "Complex models", "Nested objects and collections.", "/nested-forms"),
        ("Forms", "bi-ui-radios", "Radio & checkbox", "BsRadioGroup and BsCheckboxGroup.", "/form-groups"),

        ("Styling", "bi-palette", "Scoped CSS", "Co-located, isolated component styles.", "/scoped-css"),
        ("Styling", "bi-link-45deg", "Asset loading", "Content-addressed scoped assets.", "/asset-loading"),

        ("Data & files", "bi-cloud-arrow-down", "HttpClient + DI", "Inject HttpClient, fetch in OnMountAsync.",
            "/http"),
        ("Data & files", "bi-upload", "File upload", "Staged multipart uploads.", "/upload"),
        ("Data & files", "bi-cloud-download", "File download", "One-shot secure downloads.", "/download"),

        ("Apps", "bi-check2-square", "Todos", "A small end-to-end app.", "/todos"),
        ("Apps", "bi-braces", "IJSRuntime", "Dispatch to scoped JS modules.", "/jsruntime"),

        // Type-safe, generator-emitted route URLs (Routes.* — same namespace as these pages).
        ("Browser APIs", "bi-hdd", "Storage", "localStorage / sessionStorage, typed.", Routes.StoragePage()),
        ("Browser APIs", "bi-database", "Cookies", "document.cookie with typed options.", Routes.CookiesPage()),
        ("Browser APIs", "bi-clipboard", "Clipboard", "Copy and read text.", Routes.ClipboardPage()),
        ("Browser APIs", "bi-geo-alt", "Geolocation", "Current position, Promise-wrapped.", Routes.GeolocationPage()),
        ("Browser APIs", "bi-shield-lock", "Permissions", "Query state before prompting.", Routes.PermissionsPage()),
        ("Browser APIs", "bi-phone-vibrate", "Vibration", "Pulse the device motor.", Routes.VibrationPage()),
        ("Browser APIs", "bi-eye", "Page visibility", "Foreground/visible state.", Routes.PageVisibilityPage()),
        ("Browser APIs", "bi-info-circle", "Browser info", "onLine, language, userAgent.", Routes.NavigatorInfoPage())
    ];

    protected override RenderResult Head => Title()["Welcome — Rask"];

    protected override RenderResult Render() =>
    [
        Div(Class: "p-4 p-md-5 mb-4 rounded-3 hero-card")[
            Div(Class: "container-fluid py-3")[
                Div(Class: "hero-logo mb-4")[RaskLogo.Mark(76, "heroBolt")],
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
                    BsButton(Color: BsColor.Light, Size: BsSize.Lg, Class: "fw-semibold", OnClick: () => nav.NavigateTo("/tags"))[BsIcon(Name: BsIconName.ArrowRight, Class: "me-2"),
                        "Start with Tags"],
                    BsButton(Size: BsSize.Lg, Class: "btn-outline-light", OnClick: () => nav.NavigateTo(Routes.GuidesIndexPage()))[
                        BsIcon(Name: BsIconName.Book, Class: "me-2"), "Read the guides"],
                    A("https://github.com/pal-tamas/rask",
                        "_blank",
                        Class: "btn btn-outline-light btn-lg")[BsIcon(Name: BsIconName.Github, Class: "me-2"),
                        "Source on GitHub"]
                ]
            ]
        ],
        CodeSample(
            ["HomeWelcomeDemo.cs"],
            "The minimal page",
            Notes:
            "Generator-emitted factories build a tree. Strings convert implicitly to Child. Component.ToHtml() produces the final HTML.",
            Result: HomeWelcomeDemo()),
        H2(Class: "h4 mt-5 mb-1")["Explore the showcase"],
        P(Class: "text-secondary mb-4")[
            "Every page on the left has a runnable demo and the C# source that produced it."
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
    private IEnumerable<Child> FeatureIndex()
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
                cards.Select(f => (Child)FeatureCard(f.Icon, f.Title, f.Blurb, f.Path))
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
