using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's editor dialog — not a Flux component, an addition the owner approved — its examples with their code, and
///     its reference.
/// </summary>
[Route("editor-dialog")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiEditorDialogPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.EditorDialog.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.EditorDialog)
            .Lead("A list's record added or changed in a dialog over the list, at the list's own address: ?edit=new or "
                  + "?edit=7 opens it, so a link, a reload and Back all do what the reader expects. Not one of Flux UI's "
                  + "components: the kit's own, made of Flux's modal — a centred box, or a sheet from the right.")
            .Api(UiApi.EditorDialog)[
            UiDocsExample.Key("ui-editor-dialog").Heading("Over its list").File("EditorDialogExample.cs").Demo(EditorDialogExample).Testid("ui-editor-dialog")
                .Blurb("In(\"edit\") names the key. New supplier and each row's Edit are links to this page with the key "
                       + "set (route.With(\"edit\", id)), so the search stays. The cross, Escape, a press outside, Cancel and "
                       + "Back take the key out and the list stands as it was; Save ends with Go.Without(\"edit\")."),
            UiDocsExample.Key("ui-editor-dialog-wide").Heading("A wide sheet").File("EditorDialogWideExample.cs").Demo(EditorDialogWideExample).Testid("ui-editor-dialog-wide")
                .Blurb("Wide() opens a form with rows of its own as a sheet from the right, as wide as the screen allows "
                       + "(84rem at most). One list can hold both: each dialog its own key.")
        ];
}
