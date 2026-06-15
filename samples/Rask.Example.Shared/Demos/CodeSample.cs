using System.Collections.Concurrent;
using ColorCode;

namespace Rask.Example.Shared.Demos;

public sealed class CodeSample : Component
{
    // Source is a constant author-time string per instance, and a page can mount
    // ~15 CodeSamples that each re-render on every live diff. Memoise the tokenized
    // HTML keyed by the trimmed source so repeated renders never re-run the parser.
    // This also bounds HtmlClassFormatter construction to one-per-distinct-source.
    private static readonly ConcurrentDictionary<string, string> HighlightCache = new(StringComparer.Ordinal);

    public string? Title { get; set; }

    // Non-nullable + no initializer + no `required` keyword: the factory generator emits
    // Source as the first required positional parameter (no default), preserving the
    // existing 74 call-site shapes. The CS8618 warning is intentional — Rask's
    // post-render property assignment satisfies it at runtime. `required` is deliberately
    // omitted to keep `CodeSample(Source: ...)` as a plain positional/named argument.
#pragma warning disable CS8618
    public string Source { get; set; }
#pragma warning restore CS8618

    public Component? Result { get; set; }
    public string? Notes { get; set; }

    // Syntax highlighting is produced server-side by ColorCode: GetHtmlString tokenizes
    // the C# source into <span class="..."> markup whose classes (keyword/string/comment/…)
    // are coloured by the .sample-code rules in the app's global stylesheet (wwwroot/global.css);
    // they can't be scoped because Raw() markup carries no scope id. The result is injected via the
    // Raw() factory (verbatim, un-encoded — ColorCode already HTML-encodes token text), so
    // no client JS runs and the highlight is present in the very first render.
    private string HighlightedHtml() =>
        HighlightCache.GetOrAdd(
            Source.TrimEnd(),
            // A fresh formatter per cache-miss: HtmlClassFormatter mutates a per-instance
            // Writer field during GetHtmlString and is therefore not safe to share across
            // concurrent renders. The cache bounds this to one allocation per distinct source.
            static src => StripWrapper(new HtmlClassFormatter().GetHtmlString(src, Languages.CSharp)));

    // ColorCode wraps its output as <div class="csharp"><pre>\n …spans… \n</pre></div>.
    // We keep our own <pre class="sample-code"><code class="language-csharp"> for layout and
    // scoped styling, so peel ColorCode's wrapper and inject only the inner token spans.
    private static string StripWrapper(string html)
    {
        const string open = "<pre>";
        const string close = "</pre>";
        var start = html.IndexOf(open, StringComparison.Ordinal);
        var end = html.LastIndexOf(close, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            return html; // defensive: ColorCode's output shape changed — render it as-is.
        }

        start += open.Length;
        return html.Substring(start, end - start);
    }

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
                        Code(Class: "language-csharp")[Raw(HighlightedHtml())]
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
