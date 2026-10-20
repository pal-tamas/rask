using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Re-skinning Rask.Ui: its base gray, its accent and its typeface, each a token in the app's own theme.</summary>
[Route("theming")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiThemingPage : Component
{
    private const string BaseSnippet = """
        @theme {
          --color-zinc-50: var(--color-slate-50);
          --color-zinc-100: var(--color-slate-100);
          --color-zinc-200: var(--color-slate-200);
          --color-zinc-300: var(--color-slate-300);
          --color-zinc-400: var(--color-slate-400);
          --color-zinc-500: var(--color-slate-500);
          --color-zinc-600: var(--color-slate-600);
          --color-zinc-700: var(--color-slate-700);
          --color-zinc-800: var(--color-slate-800);
          --color-zinc-900: var(--color-slate-900);
          --color-zinc-950: var(--color-slate-950);
        }
        """;

    private const string AccentSnippet = """
        @theme {
          --color-fx-accent: var(--color-red-500);
          --color-fx-accent-content: var(--color-red-600);
          --color-fx-accent-foreground: var(--color-white);
        }

        @layer theme {
          .dark {
            --color-fx-accent: var(--color-red-500);
            --color-fx-accent-content: var(--color-red-400);
            --color-fx-accent-foreground: var(--color-white);
          }
        }
        """;

    private const string FontsOffSnippet = """
        <PropertyGroup>
          <RaskUiWriteFonts>false</RaskUiWriteFonts>
        </PropertyGroup>
        """;

    private const string OtherFaceSnippet = """
        @theme {
          --font-sans: "Geist", ui-sans-serif, system-ui, sans-serif;
        }
        """;

    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Theming.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        UiDocsHeader
            .Entry(UiDocsCatalog.Theming)
            .Lead("Rask.Ui takes Flux UI's theme: two colours and a typeface, each a token. Re-point one in your own "
                  + "@theme, after the kit's import, and every component follows — there is nothing to override."),
        UiDocsSection.Key("base").Heading("Base color")[
            Ui.Text["The base is Tailwind's zinc scale, written straight into the components: ", Code["bg-zinc-800/5"],
                    ", ", Code["border-zinc-200"], ". Point the scale at another gray and every border, ground and "
                    + "muted ink changes with it:"],
            CodeBlock.Source(BaseSnippet).Language("css")
        ],
        UiDocsSection.Key("accent").Heading("Accent color")[
            Ui.Text["The accent is three variables: the fill of a primary action, the same hue as readable text — a "
                    + "link, an accented heading — and the text on the fill. Each box below sets them for what is inside "
                    + "it; an app sets them once, for the page:"],
            UiDocsExample.Heading("Accents").File("ThemingAccentExample.cs").Demo(ThemingAccentExample)
                .Testid("ui-theming")
                .Blurb("An arbitrary-property class sets the tokens for one subtree. The default is the kit's own: "
                       + "zinc-800, and white in dark mode."),
            CodeBlock.Source(AccentSnippet).Language("css"),
            Ui.Text.Sm.Subtle["Flux names them --color-accent, --color-accent-content and --color-accent-foreground. "
                              + "Rask.Ui's carry fx- while the last daisyUI-drawn parts of the kit still define "
                              + "--color-accent with another meaning."]
        ],
        UiDocsSection.Key("font").Heading("Font")[
            Ui.Text["Rask.Ui is drawn in Inter, as Flux is, and its charts lay their labels out with Inter's metrics. "
                    + "The kit's theme names \"Inter\" first in ", Code["--font-sans"], ", and every app's build copies the "
                    + "variable face — latin and latin-ext, every weight — into ", Code["wwwroot/fonts"],
                    ", with its OFL licence beside it. No page waits on a font CDN."],
            Ui.Text["To draw in another face, re-point the token in your own theme:"],
            CodeBlock.Source(OtherFaceSnippet).Language("css"),
            Ui.Text["An app that serves its own copy of Inter, or no Inter at all, turns the copy off in its project file. "
                    + "The system stack after Inter draws until the token says otherwise:"],
            CodeBlock.Source(FontsOffSnippet).Language("xml")
        ]
    ];
}
