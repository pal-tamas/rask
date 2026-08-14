using ColorCode;
using Microsoft.JSInterop;

namespace Rask.Example.Shared;

public sealed partial class CodeSample : Component
{
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

    public new string? Title { get; set; }

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
        var ext = Path.GetExtension(file).ToLowerInvariant();
        var codeClass = ext switch
        {
            ".js" => "language-javascript",
            ".css" => "language-css",
            ".cs" => "language-csharp",
            _ => "language-plaintext",
        };
        return (file, source, SyntaxHighlighter.LanguageFor(ext), codeClass);
    }

    // Copies the raw (un-highlighted) source of the active tab. C# already holds the string,
    // so JS needs no DOM read; the button ref lets the scoped JS flash a "Copied!" affordance.
    private async Task CopyAsync()
    {
        var (_, source, _, _) = Pane(_active);
        await _js.InvokeVoidAsync("Rask.CodeSample.copy", source, _copyButton);
    }

    private new Component Header()
    {
        Component files = Files.Count == 1
            ? Span.Class("sample-code-label ms-2")[Files[0]]
            : Span.Class("sample-tabs ms-2")[
                Files.Select((file, index) => Button
                    .Type("button")
                    .Class($"sample-tab{(index == _active ? " active" : "")}")
                    .Key(file)
                    .OnClick(() => _active = index)[file])
            ];

        return Div.Class("sample-code-header")[
            Span.Class("sample-dot dot-r"),
            Span.Class("sample-dot dot-y"),
            Span.Class("sample-dot dot-g"),
            files,
            Button
                .Type("button")
                .Class("sample-copy")
                .Ref(_copyButton)
                .OnClickAsync(CopyAsync)[
                    BsIcon.Name(BsIconName.Clipboard).Class("me-1"),
                    // A real text node (not a CSS pseudo-element) so the button has an
                    // accessible name; the scoped JS swaps it to "Copied!" on click.
                    Span.Class("sample-copy-text")["Copy"]
            ]
        ];
    }

    protected override Component? Render()
    {
        var (_, activeSource, activeLanguage, codeClass) = Pane(_active);
        return BsCard.Class(Bs.Join(Shadow.Sm, Border.None, Margin.Bottom(4), "sample-card"))[
            Title is null && Notes is null
                ? null
                : BsCardHeader.Class("bg-white border-bottom")[
                    Title is null ? null : H5.Class("mb-0 fw-semibold")[Title],
                    Notes is null
                        ? null
                        : P.Class($"text-secondary small mb-0 {(Title is null ? "" : "mt-1")}")[Notes]
                ],
            // Stacked, code first: the source pane on top, the live result below (full width). Reads
            // top-to-bottom — the code you'd write, then what it renders — and never squeezes either
            // pane into a narrow column on smaller viewports.
            Div.Class("sample-code-col")[
                Header(),
                Pre.Class("sample-code m-0")[
                    Code.Class(codeClass)[
                        // A known language is tokenized server-side and injected verbatim;
                        // an unknown extension falls back to plain, HTML-encoded text.
                        activeLanguage is null
                            ? Text.Value(activeSource.TrimEnd())
                            : Raw.Value(SyntaxHighlighter.Highlight(activeSource, activeLanguage))
                    ]
                ]
            ],
            Div.Class("sample-result-col p-4")[
                Div.Class("sample-result-label")["Live result"],
                Div.Class("sample-result-body")[Result ?? null]
            ]
        ];
    }
}
