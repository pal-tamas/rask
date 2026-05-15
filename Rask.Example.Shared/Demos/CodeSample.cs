namespace Rask.Example.Shared.Demos;

public sealed class CodeSample : Component
{
    private const string CssText = """
                                   .sample-card { overflow: hidden; }
                                   .sample-code-col {
                                       background: #1f1d2b;
                                       color: #e7e3ff;
                                       display: flex;
                                       flex-direction: column;
                                   }
                                   .sample-code-header {
                                       display: flex;
                                       align-items: center;
                                       gap: 0.35rem;
                                       padding: 0.55rem 0.9rem;
                                       border-bottom: 1px solid rgba(255,255,255,0.08);
                                       background: rgba(0,0,0,0.18);
                                   }
                                   .sample-dot {
                                       width: 0.65rem;
                                       height: 0.65rem;
                                       border-radius: 50%;
                                       display: inline-block;
                                   }
                                   .dot-r { background: #ff5f57; }
                                   .dot-y { background: #febc2e; }
                                   .dot-g { background: #28c840; }
                                   .sample-code-label {
                                       font-size: 0.72rem;
                                       text-transform: uppercase;
                                       letter-spacing: 0.08em;
                                       color: rgba(255,255,255,0.45);
                                   }
                                   .sample-code {
                                       padding: 1rem 1.2rem;
                                       font-size: 0.82rem;
                                       line-height: 1.55;
                                       background: transparent;
                                       color: inherit;
                                       overflow-x: auto;
                                       flex: 1;
                                   }
                                   .sample-code code {
                                       white-space: pre;
                                       background: transparent;
                                       padding: 0;
                                       font-size: inherit;
                                   }
                                   .sample-code code.hljs {
                                       background: transparent;
                                       padding: 0;
                                   }
                                   .sample-result-col {
                                       background: #fff;
                                       display: flex;
                                       flex-direction: column;
                                   }
                                   .sample-result-label {
                                       font-size: 0.72rem;
                                       text-transform: uppercase;
                                       letter-spacing: 0.08em;
                                       color: var(--bs-secondary-color);
                                       margin-bottom: 0.6rem;
                                   }
                                   .sample-result-body { flex: 1; }
                                   @media (max-width: 767.98px) {
                                       .sample-code-col { border-bottom: 1px solid rgba(255,255,255,0.08); }
                                   }
                                   """;

    public string? Title { get; set; }
    public required string Source { get; set; }
    public Component? Result { get; set; }
    public string? Notes { get; set; }

    protected override string? Css => CssText;

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
