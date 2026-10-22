using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's rich text editor and its toolbar items as C# components: each example with its code, and its reference.</summary>
[Route("editor")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiEditorPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Editor.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Editor)
            .Lead("A toolbar over an editable area, whose value is HTML. Its engine is Tiptap on ProseMirror, as Flux's is, "
                  + "and the browser fetches it the first time an editor mounts.")
            .Api(UiApi.Editor)[
            UiDocsExample.Key("ui-editor").Heading("Rich text editor").File("EditorExample.cs").Demo(EditorExample).Testid("ui-editor")
                .Blurb("Bound, and written as it changes. The value is the reader's HTML, shown here as text."),
            UiDocsExample.Key("ui-editor-toolbar").Heading("Configuring items").File("EditorToolbarExample.cs").Demo(EditorToolbarExample).Testid("ui-editor-toolbar")
                .Blurb("Which items, in what order: | is a separator and ~ a spacer."),
            UiDocsExample.Key("ui-editor-custom").Heading("Custom items").File("EditorCustomItemExample.cs").Demo(EditorCustomItemExample).Testid("ui-editor-custom")
                .Blurb("Where Flux resolves an item's name to a Blade file, a button of your own is a child of the toolbar, and runs C#."),
            UiDocsExample.Key("ui-editor-composed").Heading("Customization").File("EditorCustomizationExample.cs").Demo(EditorCustomizationExample).Testid("ui-editor-composed")
                .Blurb("The editor composed from its parts, with a dropdown menu in its toolbar."),
            UiDocsExample.Key("ui-editor-height").Heading("Height").File("EditorHeightExample.cs").Demo(EditorHeightExample).Testid("ui-editor-height")
                .Blurb("The area is 200 to 500 px tall unless a class on its content slot says otherwise."),
            UiDocsExample.Key("ui-editor-every").Heading("Every item").File("EditorEveryItemExample.cs").Demo(EditorEveryItemExample).Testid("ui-editor-every")
                .Blurb("Each of the fifteen items Flux's toolbar names."),
            UiDocsExample.Key("ui-editor-disabled").Heading("Disabled").File("EditorDisabledExample.cs").Demo(EditorDisabledExample).Testid("ui-editor-disabled"),
            UiDocsExample.Key("ui-editor-invalid").Heading("Invalid").File("EditorInvalidExample.cs").Demo(EditorInvalidExample).Testid("ui-editor-invalid"),
            UiEditorBehaviour
        ];
}
