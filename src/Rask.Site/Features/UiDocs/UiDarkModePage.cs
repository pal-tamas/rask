using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Dark mode in Rask.Ui: Flux UI's, a <c>dark</c> class on <c>html</c> and the <c>dark:</c> utilities that follow it.</summary>
[Route("dark-mode")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiDarkModePage : Component
{
    private const string ScriptSnippet = """
        protected override Component? HeadAssets => [Title["…"], Ui.AppearanceScript, /* stylesheets */];
        """;

    private const string ApiSnippet = """
        Rask.appearance = 'light' | 'dark' | 'system'   // get or set the reader's preference
        Rask.dark = true | false                        // get or set whether the page is dark right now
        """;

    private const string VariantSnippet = """
        @custom-variant dark (&:where(.dark, .dark *));
        """;

    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.DarkMode.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        UiDocsHeader
            .Entry(UiDocsCatalog.DarkMode)
            .Lead("Dark mode is Flux UI's: a dark class on <html>, and dark: utilities that follow it. Every Rask.Ui "
                  + "component is drawn for both, and the reader chooses light, dark or their system's."),
        UiDocsExample.Heading("Light, dark or system").File("DarkModeExample.cs").Demo(DarkModeExample)
            .Testid("ui-dark-mode")
            .Blurb("Each button sets the reader's appearance in the browser. There is no C# handler, so it works on a "
                   + "prerendered page before the app has booted."),
        UiDocsSection.Key("script").Heading("Before the first paint")[
            Ui.Text[Code["RaskApp"], " and the WebAssembly host write ", Code["Ui.AppearanceScript"], " at the top of every "
                    + "page's head, as Flux's layout writes @fluxAppearance. A hand-wired host (AddRask and MapRask) writes its "
                    + "own document: put it in the root component's head assets, before the stylesheets, so a dark page "
                    + "starts dark:"],
            CodeBlock.Source(ScriptSnippet).Language("csharp"),
            Ui.Text["The reader's appearance is light, dark or system. System is the default: it follows ",
                    Code["prefers-color-scheme"], " while the page is open, and is stored as nothing at all. The other two "
                    + "are kept in localStorage under ", Code["rask.appearance"], " — ", Code[".StorageKey(\"…\")"],
                    " names another key. The class is put back after every redraw, and a change in another tab is followed."]
        ],
        UiDocsSection.Key("api").Heading("Switching it")[
            Ui.Text["Two properties, the ones Flux calls ", Code["Flux.appearance"], " and ", Code["Flux.dark"], ":"],
            CodeBlock.Source(ApiSnippet).Language("javascript"),
            Ui.Text["So a toggle is one attribute and no C# handler. That is the moon in this site's own bar:"],
            CodeBlock.Source("Ui.Button.Subtle.Icon(Ui.IconName.Moon).Attributes((\"onclick\", \"Rask.dark = !Rask.dark\"))")
                .Language("csharp")
        ],
        UiDocsSection.Key("variant").Heading("Your own dark: utilities")[
            Ui.Text["The kit's import already points ", Code["dark:"], " at the class. A stylesheet of your own that "
                    + "does not import the kit says the same thing:"],
            CodeBlock.Source(VariantSnippet).Language("css")
        ]
    ];
}
