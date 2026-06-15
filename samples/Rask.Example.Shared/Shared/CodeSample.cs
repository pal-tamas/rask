using System.Collections.Concurrent;
using ColorCode;
using Microsoft.JSInterop;

namespace Rask.Example.Shared;

public sealed class CodeSample : Component
{
    // Source is a constant author-time string per instance, and a page can mount
    // ~15 CodeSamples that each re-render on every live diff (and again on every tab
    // switch). Memoise the tokenized HTML keyed by language + trimmed source so repeated
    // renders never re-run the parser. This also bounds HtmlClassFormatter construction
    // to one-per-distinct-(language, source).
    private static readonly ConcurrentDictionary<string, string> HighlightCache = new(StringComparer.Ordinal);

    // Clipboard interop is injected via the ctor (the framework's DI seam) so Source stays
    // a plain factory parameter — a settable non-nullable service prop would become a
    // required param and clash with DI (RASK002). Mirrors ElementRefDemo's IJSRuntime ctor.
    private readonly IJSRuntime _js;

    // A stable ref to the copy button so its scoped JS can flash "Copied!" on the element.
    private readonly ElementRef _copyButton = ElementRef.New();

    // Non-nullable + no initializer + no `required` keyword: the factory generator emits
    // Source as the first required positional parameter (no default), preserving the
    // existing call-site shapes. The CS8618 warnings (here and on the ctor) are intentional —
    // Rask's post-render property assignment satisfies Source at runtime. `required` is
    // deliberately omitted to keep `CodeSample(Source: ...)` a plain positional/named argument.
#pragma warning disable CS8618
    public CodeSample(IJSRuntime js) => _js = js;

    public string? Title { get; set; }

    public string Source { get; set; }
#pragma warning restore CS8618

    // Optional sibling-language sources. When either is set the header shows a C#/JS/CSS
    // tab strip; otherwise the card keeps its single "C#" label. Both are nullable, so the
    // generator emits them as optional named factory parameters (default null).
    public string? Js { get; set; }
    public string? Css { get; set; }

    public Component? Result { get; set; }
    public string? Notes { get; set; }

    // Which language pane is visible. A plain component field (not a reactive prop): the tab
    // buttons set it and re-render through Rask's live diff — the framework way, no client JS.
    private enum Lang { Cs, Js, Css }

    private Lang _active = Lang.Cs;

    // The (raw source, ColorCode language, <code> class) triple for a pane.
    private (string Source, ILanguage Language, string CodeClass) Pane(Lang lang) => lang switch
    {
        Lang.Js => (Js!, Languages.JavaScript, "language-javascript"),
        Lang.Css => (Css!, Languages.Css, "language-css"),
        _ => (Source, Languages.CSharp, "language-csharp"),
    };

    private static string Label(Lang lang) => lang switch
    {
        Lang.Js => "JS",
        Lang.Css => "CSS",
        _ => "C#",
    };

    // C# is always first; JS/CSS only appear when their source was supplied.
    private List<Lang> PresentLanguages()
    {
        var langs = new List<Lang> { Lang.Cs };
        if (Js is not null)
        {
            langs.Add(Lang.Js);
        }

        if (Css is not null)
        {
            langs.Add(Lang.Css);
        }

        return langs;
    }

    // Syntax highlighting is produced server-side by ColorCode: GetHtmlString tokenizes the
    // source into <span class="..."> markup whose classes (keyword/string/comment/cssSelector/…)
    // are coloured by the .sample-code rules in the app's global stylesheet (wwwroot/global.css);
    // they can't be scoped because Raw() markup carries no scope id. The result is injected via
    // the Raw() factory (verbatim, un-encoded — ColorCode already HTML-encodes token text), so no
    // client JS runs and the highlight is present in the very first render.
    private static string HighlightedHtml(string source, ILanguage language)
    {
        var trimmed = source.TrimEnd();
        return HighlightCache.GetOrAdd(
            // Key by language id too: the same text tokenizes differently per language.
            $"{language.Id}\n{trimmed}",
            // A fresh formatter per cache-miss: HtmlClassFormatter mutates a per-instance
            // Writer field during GetHtmlString and is therefore not safe to share across
            // concurrent renders. The cache bounds this to one allocation per distinct key.
            _ => StripWrapper(new HtmlClassFormatter().GetHtmlString(trimmed, language)));
    }

    // ColorCode wraps its output as <div class="{lang}"><pre>\n …spans… \n</pre></div>.
    // We keep our own <pre class="sample-code"><code class="language-…"> for layout and
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

    // Copies the raw (un-highlighted) source of the active tab. C# already holds the string,
    // so JS needs no DOM read; the button ref lets the scoped JS flash a "Copied!" affordance.
    private async Task CopyAsync()
    {
        var (source, _, _) = Pane(_active);
        await _js.InvokeVoidAsync("Rask.CodeSample.copy", source, _copyButton);
    }

    private Child Header()
    {
        var present = PresentLanguages();
        Child languages = present.Count == 1
            ? Span(Class: "sample-code-label ms-2")["C#"]
            : Span(Class: "sample-tabs ms-2")[
                present.Select(lang => Button(
                    Type: "button",
                    Class: $"sample-tab{(lang == _active ? " active" : "")}",
                    Key: Label(lang),
                    OnClick: () => _active = lang)[Label(lang)])
            ];

        return Div(Class: "sample-code-header")[
            Span(Class: "sample-dot dot-r"),
            Span(Class: "sample-dot dot-y"),
            Span(Class: "sample-dot dot-g"),
            languages,
            Button(
                Type: "button",
                Class: "sample-copy",
                Ref: _copyButton,
                OnClickAsync: CopyAsync)[
                    I(Class: "bi bi-clipboard me-1"),
                    // A real text node (not a CSS pseudo-element) so the button has an
                    // accessible name; the scoped JS swaps it to "Copied!" on click.
                    Span(Class: "sample-copy-text")["Copy"]
            ]
        ];
    }

    protected override RenderResult Render()
    {
        var (activeSource, activeLanguage, codeClass) = Pane(_active);
        return Div(Class: "card shadow-sm border-0 mb-4 sample-card")[
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
                    Header(),
                    Pre(Class: "sample-code m-0")[
                        Code(Class: codeClass)[Raw(HighlightedHtml(activeSource, activeLanguage))]
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
}
