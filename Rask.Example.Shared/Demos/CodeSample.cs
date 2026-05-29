using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Example.Shared.Demos;

public sealed class CodeSample(IJSRuntime js) : Component
{
    public string? Title { get; set; }

    // Non-nullable + no initializer + no `required` keyword: the factory generator emits
    // Source as the first required positional parameter (no default), preserving the
    // existing 63 call-site shapes. The CS8618 warning is intentional — Rask's
    // ActivatorUtilities + post-render property assignment satisfies it at runtime.
    // `required` cannot be used because IJSRuntime is now ctor-injected (RASK002).
#pragma warning disable CS8618
    public string Source { get; set; }
#pragma warning restore CS8618

    public Component? Result { get; set; }
    public string? Notes { get; set; }

    // The framework collects Head contributions from every component currently in the
    // tree, dedupes by rendered HTML, and splices them into the framework-managed
    // <head> slot. Multiple CodeSample instances on a page share the same hljs <link>
    // and <script>; navigating to a page without any CodeSample drops them out of
    // <head> automatically. No user-placed marker is needed.
    protected override RenderResult Head => [
        Link(Rel: "stylesheet",
            Href: LiveOptions.PathBase + "/lib/highlightjs/atom-one-dark.min.css"),
        Script(LiveOptions.PathBase + "/lib/highlightjs/highlight.min.js")
    ];

    // The sibling `CodeSample.js` exports a `rendered` function that walks every
    // `.sample-card` on the page and highlights its <code> child via hljs. The
    // generator wraps that file as `window.Rask.CodeSample`, so a plain
    // `IJSRuntime.InvokeVoidAsync` is enough to invoke it. The hook is
    // idempotent on the JS side (skips blocks where dataset.highlighted is
    // already set) so firing on every render — including replays of cached
    // instances post-morph — does no extra work after the first highlight.
    protected override async Task OnRenderedAsync(bool firstRender) =>
        await js.InvokeVoidAsync("Rask.CodeSample.rendered", firstRender);

    protected override RenderResult Render() =>
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
