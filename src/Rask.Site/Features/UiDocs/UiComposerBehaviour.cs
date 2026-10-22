namespace Rask.Site.Features.UiDocs;

/// <summary>How a composer behaves: the keys that send, its height, its slots, and its states.</summary>
internal sealed partial class UiComposerBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Keys(),
            Height(),
            Slots()
        ];

    private static UiDocsTopic Keys() =>
        UiDocsTopic.Key("keys").Heading("The keys that send").Paragraphs([
            "Ctrl+Enter or Cmd+Enter sends the form (Flux's `cmd+enter`); `.Enter` makes Enter alone send it and leaves "
            + "Shift+Enter for a new line. The keys are the runtime's (`data-rask-submit-keys`), in the keydown itself: "
            + "no newline lands first, and the form is submitted as its submit button would submit it. Outside a form the "
            + "keys do nothing, and neither key empties the box, as on Flux's page. Typing sends nothing.",
        ]);

    private static UiDocsTopic Height() =>
        UiDocsTopic.Key("height").Heading("It grows with its text").Paragraphs([
            "Two lines unless `Rows` says otherwise; the box grows a line at a time with its text up to `MaxRows`, ten "
            + "unless set, and scrolls from there (`data-rask-grow`). A box the page empties keeps its height, as Flux's "
            + "does.",
        ]);

    private static UiDocsTopic Slots() =>
        UiDocsTopic.Key("slots").Heading("Slots and states").Paragraphs([
            "`Header`, above the box, `Footer`, `ActionsLeading` and `ActionsTrailing` take any component; `Input` takes "
            + "a `Ui.Editor`, which is then as tall as `Rows` and `MaxRows` say and bound to the composer's value.",
            "`Disabled()` locks the box and every `Ui.Button` in its slots. `Invalid()`, or a bound member the form calls "
            + "invalid, draws the red border. `Label` and `Description` wrap it in a field, and `LabelSrOnly()` and "
            + "`DescriptionSrOnly()` keep them for screen readers only; the composer is a `role=\"group\"` named by its "
            + "label.",
        ]);
}
