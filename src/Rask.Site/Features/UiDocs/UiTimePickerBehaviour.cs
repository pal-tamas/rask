namespace Rask.Site.Features.UiDocs;

/// <summary>How a time picker behaves: what it binds, the keyboard, and typed times.</summary>
internal sealed partial class UiTimePickerBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Value(),
            Keyboard(),
            Typed()
        ];

    private static UiDocsTopic Value() =>
        UiDocsTopic.Key("value").Heading("What it binds").Paragraphs([
            "`TimeOnly?` picks one time or none, `TimeOnly` always one, and a collection of `TimeOnly` several, Flux's "
            + "`multiple`. A bound time is written with the reader's next action, at once with `.Live()`. `Unavailable` "
            + "takes times and `UiTimeRange` stretches, listed but disabled; `Clearable()` puts a cross in the button "
            + "while a time is chosen.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The keyboard").Paragraphs([
            "The button is the combobox and keeps the focus. The open list holds the page still (`data-rask-lock`), and "
            + "scrolls itself to keep the active time in view while the page stays.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("Space, ArrowDown, ArrowUp", "Open the list. Enter on the closed button does nothing, as on Flux."),
                ("ArrowDown, ArrowUp", "Move the cursor through the list."),
                ("Enter", "Picks the lit time and closes; with several it switches the time and the list stays open."),
                ("Escape", "Closes."),
            ])
        ];

    private static UiDocsTopic Typed() =>
        UiDocsTopic.Key("typed").Heading("Typed times").Paragraphs([
            "The typed trigger is one field in parts, as the date picker's is: 9 is nine o'clock and moves on, `a` and `p` "
            + "set the half of the day, and a whole time reaches the binding through one hidden field as `HH:mm`.",
            "The same departure from Flux as the date picker's, by the same decision: an hour with no minute is no time, "
            + "\"Enter a valid time.\" shows under the field and the save is refused, and every part emptied is no time "
            + "chosen.",
        ]);
}
