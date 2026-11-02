namespace Rask.Site.Features.UiDocs;

/// <summary>How the confirm dialog behaves: placed once, what says no and what says yes, and what it sends.</summary>
internal sealed partial class UiConfirmBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Placing(),
            Answering(),
            Sending(),
            Words()
        ];

    private static UiDocsTopic Placing() =>
        UiDocsTopic.Key("placing").Heading("Placed once").Paragraphs([
            "`Ui.Confirm` goes in the layout, beside `Ui.Toast`, and every `.Confirm(\"…\")` under it asks in that "
            + "dialog. The call sites do not change: `.Confirm` is a step on every element, a plain `Button`, a "
            + "`Ui.Button`, a `Form`, a submit button and a `Ui.MenuItem` alike.",
            "Without it the same `.Confirm` asks in the browser's own `window.confirm`, or `window.prompt` for "
            + "`expect:`, and `heading:` is not shown.",
        ]);

    private static Component Answering() =>
        UiDocsTopic.Key("answering").Heading("No, and yes").Paragraphs([
            "Only Yes lets the action go: the same press, or the same submit by the same button. The page may render "
            + "while the reader is asked; Yes presses the button as the page stands then, and one that is gone runs "
            + "nothing. Focus goes back to the control that was pressed.",
        ])[
            UiDocsKeys.Column("The reader").Rows([
                ("presses No, the close button or Escape, or presses outside", "The action is called off. Nothing is sent."),
                ("presses Yes", "The click or the submit goes, and a form is held only now."),
                ("types into the `expect:` field", "Yes stays disabled until the answer matches exactly; then Enter is Yes."),
            ])
        ];

    private static UiDocsTopic Sending() =>
        UiDocsTopic.Key("sending").Heading("Asked in the browser").Paragraphs([
            "The dialog is rendered closed and the runtime writes the question into it and opens it (`data-rask-confirm`, "
            + "`data-rask-confirm-expect`), so opening, typing and closing send the server nothing, on both hosts. A "
            + "cancelled action sends nothing either: not the click, and not the bound fields waiting to go with it.",
            "The expected answer is written into the page, so it guards against a slip, never a secret.",
        ]);

    private static UiDocsTopic Words() =>
        UiDocsTopic.Key("words").Heading("Its words").Paragraphs([
            "`Heading`, `No` and `Yes` default from the string catalog (`ConfirmHeading`, `ConfirmNo`, `ConfirmYes`), so "
            + "a localized app needs no step at all. A question's `heading:` replaces the title for that question alone.",
            "Drawn as Flux's confirmation: `min-w-[22rem]`, a ghost button that says no and a danger button that says "
            + "yes. The answer field, and a Yes that is no submit button, are its two departures.",
        ]);
}
