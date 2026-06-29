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
    // required param and clash with the DI-only ctor (no parameterless ctor → RASK002).
    // Mirrors ElementRefDemo's IJSRuntime ctor.
    private readonly IJSRuntime _js;

    // A stable ref to the copy button so its scoped JS can flash "Copied!" on the element.
    private readonly ElementRef _copyButton = ElementRef.New();

    // Non-nullable + no initializer + no `required` keyword: the factory generator emits
    // Files as the first required positional parameter (no default), preserving the
    // existing call-site shapes. The CS8618 warnings (here and on the ctor) are intentional —
    // Rask's post-render property assignment satisfies Files at runtime. `required` is
    // deliberately omitted to keep `CodeSample(Files: ...)` a plain positional/named argument.
#pragma warning disable CS8618
    public CodeSample(IJSRuntime js) => _js = js;

    public string? Title { get; set; }

    // The demo source files to show, in tab order, as bare embedded-resource leaf names
    // (e.g. ["ElementRefDemo.cs", "ElementRefDemo.js"]). Each file gets its own tab labelled
    // with the file name; the syntax-highlight language is inferred from the extension. The
    // first file is the active tab. The verbatim text is read on demand via EmbeddedSource so
    // the snippet always compiles and never drifts from what actually runs.
    public IReadOnlyList<string> Files { get; set; }
#pragma warning restore CS8618

    public Component? Result { get; set; }
    public string? Notes { get; set; }

    // Index of the visible tab into Files. A plain component field (not a reactive prop): the
    // tab buttons set it and re-render through Rask's live diff — the framework way, no client JS.
    private int _active;

    // The (file name, raw source, ColorCode language, <code> class) tuple for a tab. The
    // highlight language is inferred from the file extension; an unknown extension falls back
    // to plain (un-tokenized) text rendered through the Text-encoding code path.
    private (string File, string Source, ILanguage? Language, string CodeClass) Pane(int index)
    {
        var file = Files[index];
        var source = EmbeddedSource.Read(file);
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".js" => (file, source, Languages.JavaScript, "language-javascript"),
            ".css" => (file, source, Languages.Css, "language-css"),
            ".cs" => (file, source, Languages.CSharp, "language-csharp"),
            _ => (file, source, null, "language-plaintext"),
        };
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
        var (_, source, _, _) = Pane(_active);
        await _js.InvokeVoidAsync("Rask.CodeSample.copy", source, _copyButton);
    }

    private Child Header()
    {
        Child files = Files.Count == 1
            ? Span(Class: "sample-code-label ms-2")[Files[0]]
            : Span(Class: "sample-tabs ms-2")[
                Files.Select((file, index) => Button(
                    Type: "button",
                    Class: $"sample-tab{(index == _active ? " active" : "")}",
                    Key: file,
                    OnClick: () => _active = index)[file])
            ];

        return Div(Class: "sample-code-header")[
            Span(Class: "sample-dot dot-r"),
            Span(Class: "sample-dot dot-y"),
            Span(Class: "sample-dot dot-g"),
            files,
            Button(
                Type: "button",
                Class: "sample-copy",
                Ref: _copyButton,
                OnClickAsync: CopyAsync)[
                    BsIcon(Name: BsIconName.Clipboard, Class: "me-1"),
                    // A real text node (not a CSS pseudo-element) so the button has an
                    // accessible name; the scoped JS swaps it to "Copied!" on click.
                    Span(Class: "sample-copy-text")["Copy"]
            ]
        ];
    }

    protected override RenderResult Render()
    {
        var (_, activeSource, activeLanguage, codeClass) = Pane(_active);
        return BsCard(Class: Bs.Join(Shadow.Sm, Border.None, Margin.Bottom(4), "sample-card"))[
            Title is null && Notes is null
                ? Fragment()
                : BsCardHeader(Class: "bg-white border-bottom")[
                    Title is null ? Fragment() : H5(Class: "mb-0 fw-semibold")[Title],
                    Notes is null
                        ? Fragment()
                        : P(Class: $"text-secondary small mb-0 {(Title is null ? "" : "mt-1")}")[Notes]
                ],
            Div(Class: "row g-0")[
                Div(Class: "col-md-7 sample-code-col")[
                    Header(),
                    Pre(Class: "sample-code m-0")[
                        Code(Class: codeClass)[
                            // A known language is tokenized server-side and injected verbatim;
                            // an unknown extension falls back to plain, HTML-encoded text.
                            activeLanguage is null
                                ? Text(activeSource.TrimEnd())
                                : Raw(HighlightedHtml(activeSource, activeLanguage))
                        ]
                    ]
                ],
                Div(Class: "col-md-5 sample-result-col p-4")[
                    Div(Class: "sample-result-label")[
                        BsIcon(Name: BsIconName.Eye, Class: "me-1"),
                        "Live result"
                    ],
                    Div(Class: "sample-result-body")[Result ?? Fragment()]
                ]
            ]
        ];
    }
}
