using ColorCode;
using Microsoft.JSInterop;

namespace Rask.Site;

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
        await _js.InvokeVoidAsync("Rask.CodeSample.copy", source, _copyButton);
    }

    // The header sits in the row daisyUI's `mockup-code` opens with its three window dots, to their right. The dots
    // are the component's own ::before now — the three spans and their literal traffic-light colours are gone.
    private Component Header()
    {
        Component files = Files.Count == 1
            ? Span.Class("sample-code-label font-mono text-xs opacity-60")[Files[0]]
            // One row that scrolls sideways rather than wrapping: the header sits in mockup-code's fixed top band, and
            // a second row of tabs on a phone spilled into the code beneath it.
            : Div.Class("sample-tabs tabs tabs-xs min-w-0 flex-nowrap overflow-x-auto")[
                Files.Select((file, index) => Button
                    .Type("button")
                    // "sample-tab active" stays one run of text: a unit test reads the pair, and the browser suite
                    // selects on .sample-tab. `tab`/`tab-active` are what daisyUI draws.
                    .Class(index == _active
                        ? "sample-tab active tab tab-active shrink-0 font-mono text-[#e7e3ff]!"
                        : "sample-tab tab shrink-0 font-mono text-[#e7e3ff]! opacity-60 hover:opacity-100")
                    .Key(file)
                    .OnClick(() => _active = index)[file])
            ];

        return Div.Class("sample-code-header absolute inset-x-0 top-0 flex h-11 items-center gap-1 ps-20 pe-3")[
            files,
            Button
                .Type("button")
                // `copied` is toggled by the scoped script for the moment it flashes "Copied!".
                .Class("sample-copy btn btn-ghost btn-xs ms-auto shrink-0 font-normal text-[#e7e3ff]! opacity-60 hover:opacity-100 [&.copied]:text-success! [&.copied]:opacity-100")
                .Ref(_copyButton)
                .OnClick(CopyAsync)[
                    Ui.Icon.Name(Ui.IconName.Clipboard).Class("size-4"),
                    // A real text node (not a CSS pseudo-element) so the button has an
                    // accessible name; the scoped JS swaps it to "Copied!" on click.
                    Span.Class("sample-copy-text")["Copy"]
            ]
        ];
    }

    protected override Component? Render()
    {
        var (_, activeSource, activeLanguage, codeClass) = Pane(_active);
        // daisyUI's `card` supplies the panel: the flex column, the --radius-box corner and the focus
        // ring. What is left on the element is the border, the surface and the overflow clip that keeps
        // the dark code pane inside that corner — .sample-card used to carry the radius as an
        // `!important` override of the kit card's, which is what an unlayered rule needs to beat a utility.
        // The name stays: five assertions across the unit and browser suites select on it.
        return Div.Class(
            "sample-card card mb-4 overflow-hidden border border-ui-line bg-ui-bg shadow-sm")[
            Title is null && Notes is null
                ? null
                // bg-ui-bg, not the bg-white this used to hard-code: the panel is the palette's, and a
                // literal white was the one colour on this card that could not follow a theme.
                : Div.Class("border-b border-ui-line px-5 py-3 font-medium bg-ui-bg")[
                    Title is null ? null : H5.Class("mb-0 font-semibold")[Title],
                    Notes is null
                        ? null
                        : P.Class($"text-ui-muted text-sm mb-0 {(Title is null ? "" : "mt-1")}")[Notes]
                ],
            // Stacked, code first: the source pane on top, the live result below (full width). Reads
            // top-to-bottom — the code you'd write, then what it renders — and never squeezes either
            // pane into a narrow column on smaller viewports.
            // daisyUI's `mockup-code`: the window chrome and the code pane's spacing. The ground stays the site's ink
            // rather than the theme's `neutral` because the syntax colours in global.css are one palette tuned for
            // that ink, and a neutral that is light in some themes would put them on a background they fail on.
            Div.Class("sample-code-col mockup-code relative min-w-0 rounded-none bg-(--rask-ink) text-[#e7e3ff]")[
                Header(),
                Pre.Class("sample-code m-0 overflow-x-auto px-5 pb-1 text-[0.82rem] leading-relaxed")[
                    Code.Class(codeClass)[
                        // A known language is tokenized server-side and injected verbatim;
                        // an unknown extension falls back to plain, HTML-encoded text.
                        activeLanguage is null
                            ? Text.Value(activeSource.TrimEnd())
                            : Raw.Value(SyntaxHighlighter.Highlight(activeSource, activeLanguage))
                    ]
                ]
            ],
            Div.Class("sample-result-col flex flex-col border-t border-ui-line bg-ui-bg p-4")[
                // The output side of the pairing: daisyUI's `status` dot, pulsing unless the visitor asked for less
                // motion, beside a mono label that rhymes with the code header above.
                Div.Class("sample-result-label mb-3 inline-flex items-center gap-2 font-mono text-[0.68rem] uppercase tracking-[0.12em] text-ui-brand-ink")[
                    Span.Class("status status-primary motion-safe:animate-pulse").Aria(new Dictionary<string, string?> { ["hidden"] = "true" }),
                    "Live result"
                ],
                Div.Class("sample-result-body flex-1")[Result ?? null]
            ]
        ];
    }
}
