using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's context menu as a C# component: its example with its own code, and its reference.</summary>
[Route("context")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiContextPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Context.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Context)
            .Lead("The menu a dropdown opens, opened by a right-click instead: the first child of Ui.Context is "
                  + "the area, the second the Ui.Menu, with the same rows and the same keyboard.")
            .Api(UiApi.Context)[
            UiDocsExample.Key("ui-context-menu").Heading("Context menu").File("ContextExample.cs").Demo(ContextExample).Testid("ui-context-menu")
                .Blurb("Right-click the area. The runtime opens the menu at the pointer at once, on either host, and "
                       + "the ContextMenu key or Shift+F10 opens it at the focused element. iOS never fires the "
                       + "event, so nothing in it should be the only way to do something."),
            UiContextBehaviour
        ];
}
