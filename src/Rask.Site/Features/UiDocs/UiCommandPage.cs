using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's command palette as a C# component: each example with its own code, and its reference.</summary>
[Route("command")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiCommandPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Command.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Command)
            .Lead("A searchable list of commands. Typing narrows it, the arrows and the pointer move the lit "
                  + "command and Enter runs it, all in the browser: nothing is sent until a command runs.")
            .Api(UiApi.Command)[
            UiDocsExample.Key("ui-command").Heading("Command palette").File("CommandExample.cs").Demo(CommandExample).Testid("ui-command")
                .Blurb("A Ui.CommandInput over the Ui.CommandItems. A pressed command empties the box and the "
                       + "palette stays where it is."),
            UiDocsExample.Key("ui-command-modal").Heading("As a modal").File("CommandModalExample.cs").Demo(CommandModalExample).Testid("ui-command-modal")
                .Blurb("A bare Ui.Modal around it, opened by its trigger or from anywhere on the page with ⌘K "
                       + "(Ctrl K off a Mac). Closable puts the close button on the box."),
            UiCommandBehaviour
        ];
}
