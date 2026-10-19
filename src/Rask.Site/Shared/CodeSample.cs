using System.Globalization;
using ColorCode;

namespace Rask.Site;

public sealed partial class CodeSample : Component
{
    // A stable ref to the copy button so its scoped JS can flash "Copied!" on the element.
    private readonly ElementRef _copyButton = ElementRef.New();

    // Non-nullable + no initializer + no `required` keyword: the factory generator emits
    // Files as the first required positional parameter (no default), preserving the
    // existing call-site shapes. The CS8618 warning is intentional —
    // Rask's post-render property assignment satisfies Files at runtime. `required` is
    // deliberately omitted to keep `CodeSample(Files: ...)` a plain positional/named argument.
#pragma warning disable CS8618
    public string? Title { get; set; }

    // The demo source files to show, in tab order, as bare embedded-resource leaf names
    // (e.g. ["ElementRefDemo.cs", "ElementRefDemo.ts"]). Each file gets its own tab labelled
    // with the file name; the syntax-highlight language is inferred from the extension. The
    // first file is the active tab. The verbatim text is read on demand via EmbeddedSource so
    // the snippet always compiles and never drifts from what actually runs.
    public IReadOnlyList<string> Files { get; set; }
#pragma warning restore CS8618

    public Component? Result { get; set; }
    public string? Notes { get; set; }

    // Index of the visible tab into Files. A plain component field (not a reactive prop): the
    // tabs' OnChange sets it and re-renders through Rask's live diff.
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
            ".ts" => "language-typescript",
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
        await Copy(source, _copyButton);
    }

    private Component? TitleLine() => Title is null ? null : H5.Class("mb-0 font-semibold")[Title];

    private Component? NotesLine()
    {
        if (Notes is null)
        {
            return null;
        }

        var spacing = Title is null ? "" : "mt-1";
        return P.Class($"text-ui-muted text-sm mb-0 {spacing}")[Notes];
    }

    // The header is a strip above the code: the file name, or Flux's small segmented tabs when there are several
    // files, and a ghost button that copies the file shown.
    private Component Header()
    {
        Component files = Files.Count == 1
            ? Span.Class("sample-code-label truncate font-mono text-xs text-ui-muted")[Files[0]]
            // The row scrolls sideways rather than wrapping: a second row of tabs on a phone is a second header.
            : Div.Class("min-w-0 overflow-x-auto")[
                Ui.Tabs.Segmented.Size(Ui.TabsSize.Sm)
                    .Class("sample-tabs")
                    .Value(TabName(_active))
                    .OnChange(name => _active = int.Parse(name, CultureInfo.InvariantCulture))[
                    Files.Select((file, index) => Ui.Tab.Key(file).Name(TabName(index)).Class("sample-tab font-mono")[file])
                ]
            ];

        return Div.Class("sample-code-header flex min-h-11 items-center gap-2 border-b border-ui-line bg-ui-well px-3 py-1.5")[
            files,
            Ui.Button.Ghost.Sm
                .Icon(Ui.IconName.ClipboardDocumentCheck)
                // `copied` is toggled by the scoped script for the moment it flashes "Copied!".
                .Class("sample-copy ms-auto shrink-0 [&.copied]:text-green-600 dark:[&.copied]:text-green-400")
                .Ref(_copyButton)
                .OnClick(CopyAsync)[
                    // A real text node (not a CSS pseudo-element) so the button has an
                    // accessible name; the scoped JS swaps it to "Copied!" on click.
                    Span.Class("sample-copy-text")["Copy"]
            ]
        ];
    }

    private static string TabName(int index) => index.ToString(CultureInfo.InvariantCulture);

    protected override Component? Render()
    {
        var (_, activeSource, activeLanguage, codeClass) = Pane(_active);
        // A panel drawn as Flux's card is: hairline border, rounded corners, the overflow clip that keeps the dark
        // code pane inside them. The name stays: five assertions across the unit and browser suites select on it.
        return Div.Class(
            "sample-card mb-4 flex flex-col overflow-hidden rounded-xl border border-ui-line bg-ui-bg shadow-xs")[
            Title is null && Notes is null
                ? null
                : Div.Class("border-b border-ui-line px-5 py-3 font-medium bg-ui-bg")[
                    TitleLine(),
                    NotesLine()
                ],
            // Stacked, code first: the source pane on top, the live result below (full width). Reads
            // top-to-bottom — the code you'd write, then what it renders — and never squeezes either
            // pane into a narrow column on smaller viewports.
            Div.Class("sample-code-col relative min-w-0")[
                Header(),
                // The ground stays the site's ink in both schemes, because the syntax colours in global.css are one
                // palette tuned for it.
                Pre.Class("sample-code m-0 overflow-x-auto bg-(--rask-ink) px-5 py-4 text-[0.82rem] leading-relaxed text-[#e7e3ff]")[
                    Code.Class(codeClass)[
                        // A known language is tokenized server-side and injected verbatim, while
                        // an unknown extension falls back to plain, HTML-encoded text.
                        activeLanguage is null
                            ? Text.Value(activeSource.TrimEnd())
                            : Raw.Value(SyntaxHighlighter.Highlight(activeSource, activeLanguage))
                    ]
                ]
            ],
            Div.Class("sample-result-col flex flex-col border-t border-ui-line bg-ui-bg p-4")[
                // The output side of the pairing: a dot in the brand colour, pulsing unless the visitor asked for
                // less motion, beside a mono label that rhymes with the code header above.
                Div.Class("sample-result-label mb-3 inline-flex items-center gap-2 font-mono text-[0.68rem] uppercase tracking-[0.12em] text-ui-brand-ink")[
                    Span.Class("sample-dot size-2 rounded-full bg-ui-brand motion-safe:animate-pulse").Aria("hidden", "true"),
                    "Live result"
                ],
                Div.Class("sample-result-body flex-1")[Result ?? null]
            ]
        ];
    }
}
