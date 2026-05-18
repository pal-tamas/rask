namespace Rask.Example.Shared.Demos;

public sealed class CodeSample : Component
{
    public string? Title { get; set; }
    public required string Source { get; set; }
    public Component? Result { get; set; }
    public string? Notes { get; set; }

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
