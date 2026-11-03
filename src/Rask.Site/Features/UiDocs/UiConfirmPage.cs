using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's confirm dialog — not a Flux component, an addition the owner approved — each example with its code,
///     the way it behaves, and its reference.
/// </summary>
[Route("confirm")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiConfirmPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Confirm.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Confirm)
            .Lead("The dialog an action's .Confirm(\"…\") asks in, in place of the browser's own confirm and prompt. "
                  + "Place Ui.Confirm in the layout once and every .Confirm on every page asks through it. Not one of "
                  + "Flux UI's components: the kit's own, a Ui.Modal drawn as Flux's confirmation.")
            .Api(UiApi.Confirm)[
            UiDocsExample.Key("ui-confirm").Heading("Confirm an action").File("ConfirmExample.cs").Demo(ConfirmExample).Testid("ui-confirm")
                .Blurb("This tab's layout places a Ui.Confirm, so the button asks in it. heading: is the question's own "
                       + "title; No, the close button, Escape and a press outside call the delete off, and Yes lets the "
                       + "same press go."),
            UiDocsExample.Key("ui-confirm-expect").Heading("Type to confirm").File("ConfirmExpectExample.cs").Demo(ConfirmExpectExample).Testid("ui-confirm-expect")
                .Blurb("With expect: the dialog shows a field, and Yes waits until the answer is typed exactly, case and "
                       + "all. Livewire's wire:confirm.prompt."),
            UiDocsExample.Key("ui-confirm-form").Heading("On a form").File("ConfirmFormExample.cs").Demo(ConfirmFormExample).Testid("ui-confirm-form")
                .Blurb("On a form it asks before the submit, and the form is held only once Yes lets it go. On a submit "
                       + "button it asks before the submit that button makes."),
            UiDocsExample.Key("ui-confirm-words").Heading("Your own words").File("ConfirmWordsExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.ConfirmWordsDemoPage()).Title("A layout whose Ui.Confirm says Nem and Igen")).Testid("ui-confirm-words")
                .Blurb("Heading, No and Yes are the layout's, for every question on the page; a question with heading: "
                       + "brings its own title. A layout of its own, framed, because a page holds one dialog."),
            UiConfirmBehaviour
        ];
}
