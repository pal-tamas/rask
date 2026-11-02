using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's leave dialog for a form with unsaved changes — not a Flux component, an addition the owner approved —
///     each example with its code, the way it behaves, and its reference.
/// </summary>
[Route("confirm-leave")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiConfirmLeavePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.ConfirmLeave.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.ConfirmLeave)
            .Lead("The dialog a form's ConfirmLeave(\"…\") asks in before the reader walks away from what they typed, in "
                  + "place of the browser's own confirm. Place Ui.ConfirmLeave in the layout once and every guarded form "
                  + "asks through it. Not one of Flux UI's components: the kit's own, a Ui.Modal drawn as Flux's "
                  + "confirmation.")
            .Api(UiApi.ConfirmLeave)[
            UiDocsExample.Key("ui-confirm-leave").Heading("Ask before leaving").File("ConfirmLeaveExample.cs").Demo(ConfirmLeaveExample).Testid("ui-confirm-leave")
                .Blurb("Type a note, then follow any link in the sidebar or press Back: the form's message is the "
                       + "dialog's title. Stay keeps the page exactly as it was; Save ends the guard until the next edit."),
            UiDocsExample.Key("ui-confirm-leave-words").Heading("Your own words").File("ConfirmLeaveWordsExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.ConfirmLeaveWordsDemoPage()).Title("A layout whose Ui.ConfirmLeave says Nem and Igen")).Testid("ui-confirm-leave-words")
                .Blurb("Stay and Leave name the two buttons, and Heading puts a short title over the form's question, "
                       + "the shape Ui.Confirm always has. A layout of its own, framed, because a page holds one dialog: "
                       + "type, then follow Tovább."),
            UiConfirmLeaveBehaviour
        ];
}
