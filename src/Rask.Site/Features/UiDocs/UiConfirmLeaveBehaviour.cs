namespace Rask.Site.Features.UiDocs;

/// <summary>How the leave dialog behaves: when a form counts as unsaved, what asks, staying and leaving.</summary>
internal sealed partial class UiConfirmLeaveBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Placing(),
            Unsaved(),
            Asking(),
            Answering()
        ];

    private static UiDocsTopic Placing() =>
        UiDocsTopic.Key("placing").Heading("Placed once").Paragraphs([
            "`Ui.ConfirmLeave` goes in the layout, and every form with `ConfirmLeave(\"…\")` under it asks in that "
            + "dialog. The forms do not change. Without it a guarded form asks in the browser's own `window.confirm`.",
        ]);

    private static UiDocsTopic Unsaved() =>
        UiDocsTopic.Key("unsaved").Heading("When a form is unsaved").Paragraphs([
            "From the first thing typed or changed in it, decided in the browser, so it holds for a value the server "
            + "has not seen yet. It is no comparison with the first values: typing a value and typing it back still counts.",
            "A submit that passed validation and whose handler returned makes it clean again, until the next edit. A "
            + "navigation your own handler makes never asks, so \"save, then go to the list\" needs nothing more. With "
            + "several guarded forms on a page, the first unsaved one asks, in its own words.",
        ]);

    private static Component Asking() =>
        UiDocsTopic.Key("asking").Heading("What asks").Paragraphs([
            "Every way out of the page asks first, while the form is unsaved:",
        ])[
            UiDocsKeys.Column("The reader").Rows([
                ("follows a link inside the app", "This dialog, with the form's message."),
                ("is navigated by front-end code (`__raskHost.navigate(…)`)", "This dialog."),
                ("presses Back or Forward", "This dialog; staying puts the address and the history back."),
                ("closes the `Ui.Modal` the form is in", "This dialog; staying keeps the modal open with what was typed."),
                ("closes the tab, reloads, or follows a link out of the app", "The browser's own prompt, whose text no page can set."),
            ])
        ];

    private static UiDocsTopic Answering() =>
        UiDocsTopic.Key("answering").Heading("Staying, and leaving").Paragraphs([
            "Stay, the close button, Escape and a press outside all keep the reader on the page exactly as it was, typed "
            + "values included, and send nothing. Leave carries on with the navigation that was asked for: the same "
            + "link, the same history entry Back was going to.",
            "The dialog is rendered closed and opened in the browser, never lazily, because what it guards is what the "
            + "server has not seen. Unset, `Heading` leaves the form's question as the title, with room kept for the "
            + "close button; `Stay` and `Leave` default from the string catalog (`ConfirmLeaveStay`, `ConfirmLeaveLeave`).",
        ]);
}
