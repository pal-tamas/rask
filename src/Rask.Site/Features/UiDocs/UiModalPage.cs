using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's modal as a C# component: each example with its own code, and its reference.</summary>
[Route("modal")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiModalPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Modal.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Modal)
            .Lead("A real modal <dialog> with a Name, opened by the button inside a Ui.ModalTrigger of that name. "
                  + "No handler runs: the browser gives it the top layer, an inert page behind it, Escape, and "
                  + "focus back on the trigger when it closes, on a prerendered page too.")
            .Api(UiApi.Modal)[
            UiDocsExample.Key("ui-modal").Heading("Modal").File("ModalExample.cs").Demo(ModalExample).Testid("ui-modal")
                .Blurb("The modal draws the panel and the close button; what it holds is yours. A width in Class "
                       + "replaces its own. OnClose hears every way it closed."),
            UiDocsExample.Key("ui-modal-state").Heading("Data binding").File("ModalOpenExample.cs").Demo(ModalOpenExample).Testid("ui-modal-state")
                .Blurb("For when C# decides it should appear: Open is the page's field, Flux's wire:model, and it is "
                       + "the same modal. OnCancel hears a dismissal, Escape or a click outside, before OnClose."),
            UiDocsExample.Key("ui-modal-dismissible").Heading("Disable click outside").File("ModalDismissibleExample.cs").Demo(ModalDismissibleExample).Testid("ui-modal-dismissible")
                .Blurb("Dismissible(false) keeps a stray click outside from losing what is being edited."),
            UiDocsExample.Key("ui-modal-escapable").Heading("Disable escape").File("ModalEscapableExample.cs").Demo(ModalEscapableExample).Testid("ui-modal-escapable")
                .Blurb("Escapable(false) takes Escape away; Closable(false) takes the corner button."),
            UiDocsExample.Key("ui-modal-scroll").Heading("Scrolling long content").File("ModalScrollExample.cs").Demo(ModalScrollExample).Testid("ui-modal-scroll")
                .Blurb("Scroll Body lets a long modal run past the bottom of the screen, so it is plain there is "
                       + "more."),
            UiDocsExample.Key("ui-modal-confirm").Heading("Confirmation").File("ModalConfirmationExample.cs").Demo(ModalConfirmationExample).Testid("ui-modal-confirm")
                .Blurb("Ask before a dangerous action. Ui.ModalClose makes the button inside it close the modal: a "
                       + "close, not a dismissal, and the button's own handler still runs."),
            UiDocsExample.Key("ui-modal-confirm-action").Heading("Confirm an action").File("ModalConfirmActionExample.cs").Demo(ModalConfirmActionExample).Testid("ui-modal-confirm-action")
                .Blurb("Not Flux's, and drawn as the confirmation above: place Ui.Confirm in the layout once, and "
                       + ".Confirm(\"…\") on any button or form asks in it instead of the browser. With expect: Yes "
                       + "waits for the answer typed exactly. Opening it, typing and No send nothing; Yes lets the "
                       + "press go."),
            UiDocsExample.Key("ui-modal-flyout").Heading("Flyout").File("ModalFlyoutExample.cs").Demo(ModalFlyoutExample).Testid("ui-modal-flyout")
                .Blurb("Flyout anchors it to an edge at full height, for longer forms."),
            UiDocsExample.Key("ui-modal-flyout-position").Heading("Flyout positioning").File("ModalFlyoutPositionExample.cs").Demo(ModalFlyoutPositionExample).Testid("ui-modal-flyout-position")
                .Blurb("The right edge by default; Left or Bottom when asked."),
            UiDocsExample.Key("ui-modal-floating").Heading("Floating flyout").File("ModalFloatingExample.cs").Demo(ModalFloatingExample).Testid("ui-modal-floating")
                .Blurb("The Floating variant stands the flyout off the edges of the viewport, rounded and shadowed "
                       + "like a panel."),
            UiModalBehaviour
        ];
}
