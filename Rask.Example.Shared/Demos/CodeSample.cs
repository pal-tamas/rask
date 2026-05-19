namespace Rask.Example.Shared.Demos;

public sealed class CodeSample : Component
{
    private const string HljsBase = "https://cdn.jsdelivr.net/gh/highlightjs/cdn-release@11.10.0/build/";

    public string? Title { get; set; }
    public required string Source { get; set; }
    public Component? Result { get; set; }
    public string? Notes { get; set; }

    // The framework collects Head contributions from every component currently in the
    // tree, dedupes by rendered HTML, and splices them into <head> via the
    // RaskHeadAssets() sentinel placed in App.cs. Multiple CodeSample instances on a
    // page share the same hljs <link> and <script>; navigating to a page without any
    // CodeSample drops them out of <head>.
    protected override Component? Head => Fragment()[
        Link(Rel: "stylesheet",
            Href: HljsBase + "styles/atom-one-dark.min.css",
            CrossOrigin: "anonymous"),
        Script(Src: HljsBase + "highlight.min.js",
            CrossOrigin: "anonymous")
    ];

    // The framework no longer auto-fires scoped-JS hooks. Opt in by calling
    // InvokeJs from a lifecycle hook — OnRendered fires after every render with a
    // firstRender flag we plumb through to rendered(el, firstRender) in CodeSample.js.
    // The hook itself is idempotent for hljs so re-firing on subsequent renders is
    // harmless. The method name "rendered" is a CodeSample convention — any name
    // works because InvokeJs dispatches by name to the corresponding `export
    // function` in the sibling .js.
    protected override void OnRendered(bool firstRender) => InvokeJs("rendered", firstRender);

    protected override Component Render() =>
        Div(Class: "card shadow-sm border-0 mb-4 sample-card")[
            Title is null && Notes is null
                ? Fragment()
                : Div(Class: "card-header bg-white border-bottom")[
                    Title is null ? Fragment() : H5(Class: "mb-0 fw-semibold")[Title],
                    Notes is null
                        ? Fragment()
                        : P(Class: $"text-secondary small mb-0 {(Title is null ? "" : "mt-1")}")[Notes]
                ],
            Div(Class: "row g-0")[
                Div(Class: "col-md-7 sample-code-col")[
                    Div(Class: "sample-code-header")[
                        Span(Class: "sample-dot dot-r"),
                        Span(Class: "sample-dot dot-y"),
                        Span(Class: "sample-dot dot-g"),
                        Span(Class: "sample-code-label ms-2")["C#"]
                    ],
                    Pre(Class: "sample-code m-0")[
                        Code(Class: "language-csharp")[Source.TrimEnd()]
                    ]
                ],
                Div(Class: "col-md-5 sample-result-col p-4")[
                    Div(Class: "sample-result-label")[
                        I(Class: "bi bi-eye me-1"),
                        "Live result"
                    ],
                    Div(Class: "sample-result-body")[Result ?? Fragment()]
                ]
            ]
        ];
}
