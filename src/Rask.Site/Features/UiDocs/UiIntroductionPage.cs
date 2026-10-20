using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>The UI tab's front page: what Rask.Ui is, how it follows Flux UI, and how an app takes it in.</summary>
[Route("")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiIntroductionPage : Component
{
    private const string Import = """
        @import "./vendor/rask-ui.css";
        """;

    private const string Stylesheet = """
        protected override Component? HeadAssets =>
        [
            Ui.AppearanceScript,
            Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css"),
        ];
        """;

    private const string ThemeScope = """
        protected override Component Shell(Component head, Component body) =>
            Html.Lang("en").Attributes((UiStylesheet.ThemeScopeAttribute, ""))[head, body];
        """;

    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Introduction.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        UiDocsHeader
            .Entry(UiDocsCatalog.Introduction)
            .Lead("Rask.Ui is Flux UI's component catalogue as C# components: the same names, the same props, the same "
                  + "look and the same keyboard, written as a chain. It runs on the ASP.NET Core host and in the browser on "
                  + "WebAssembly, and it ships its styles inside the package — no npm, no Tailwind config."),
        UiDocsExample.Heading("A first component").File("IntroductionExample.cs").Demo(IntroductionExample)
            .Testid("ui-intro-demo")
            .Blurb("The name and the count are plain fields on the component: a press runs C#, and the live diff "
                   + "redraws what changed."),
        UiDocsSection.Key("Install").Heading("Install")[
            Ui.Text["An app made with ", Code["rask new"], " has it already: the template imports the kit into its one "
                    + "stylesheet and turns its theme on. Anywhere else, add the package:"],
            CodeBlock.Source("dotnet add package Rask.Ui").Language("bash"),
            Ui.Text["Every component is reached from ", Code["Ui"], ", in the ", Code["Rask"], " namespace every template "
                    + "already imports: ", Code["Ui.Button.Primary[\"Save\"]"], "."]],
        UiDocsSection.Key("Wiring a host by hand").Heading("Wiring a host by hand")[
            Ui.Text["Only for a host wired with ", Code["AddRask()"], " and ", Code["MapRask<App>()"], ", or an App "
                    + "that writes its own shell. Two things, and forgetting either draws the right markup with no style."],
            Ui.Text["First, take the kit into the app's stylesheet, in place of ", Code["@import \"tailwindcss\";"],
                    ". The build writes the kit's Tailwind sources beside it before Tailwind runs, so the one sheet the app "
                    + "compiles carries the kit's theme, its dark variant and every class its components write:"],
            CodeBlock.Source(Import).Language("css"),
            Ui.Text["Link that sheet, and nothing else:"],
            CodeBlock.Source(Stylesheet).Language("csharp"),
            Ui.Text["Second, turn the theme on. Nothing has a colour until an ancestor carries the theme scope, so "
                    + "referencing the package cannot repaint an app that only wanted one button:"],
            CodeBlock.Source(ThemeScope).Language("csharp")],
        UiDocsSection.Key("How it follows Flux UI").Heading("How it follows Flux UI")[
            Ui.Text["Each component is built from Flux UI's public docs — every example on its page, measured in a "
                    + "browser in light and dark — and a parity test holds it to them. The names carry over by rule:"],
            Ui.Table.Class("mt-2")[
                Ui.TableColumns[Ui.TableColumn["Flux UI"], Ui.TableColumn["Rask.Ui"]],
                Ui.TableRows[
                    Mapping("flux:button", "Ui.Button"),
                    Mapping("flux:button.group", "Ui.ButtonGroup"),
                    Mapping("variant=\"primary\" size=\"sm\"", ".Primary.Sm"),
                    Mapping("icon=\"plus\"", ".Icon(Ui.IconName.Plus)"),
                    Mapping("wire:click=\"save\"", ".OnClick(Save)"),
                    Mapping("wire:model=\"name\"", ".Bind(() => model.Name)"),
                    Mapping("<x-slot name=\"actions\">", ".Actions(…)")
                ]
            ],
            Ui.Text["What Flux does with a script, Rask.Ui leaves to the browser where it can: a modal is a ",
                    Code["<dialog>"], ", a menu a popover, a sidebar a checkbox, so they work on a prerendered page before "
                    + "anything has loaded. Components bring their padding, borders and colour, never an outer margin: "
                    + "the page spaces them."]],
        UiDocsSection.Key("Next").Heading("Next")[
            Div.Class("flex flex-wrap gap-2")[
                Ui.Button.Href(PageMeta.LinkTo(UiDocsCatalog.Theming.Path)).IconTrailing(Ui.IconName.ArrowRight)["Theming"],
                Ui.Button.Href(PageMeta.LinkTo(UiDocsCatalog.DarkMode.Path)).IconTrailing(Ui.IconName.ArrowRight)["Dark mode"],
                Ui.Button.Href(PageMeta.LinkTo(UiDocsCatalog.Button.Path)).IconTrailing(Ui.IconName.ArrowRight)["Button"]
            ]]
    ];

    private static Component Mapping(string flux, string rask) =>
        Ui.TableRow.Key(flux)[Ui.TableCell[Code[flux]], Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[Code[rask]]];
}
