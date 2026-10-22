namespace Rask.Site.Features.UiDocs;

/// <summary>
///     How a date picker behaves: the popup, the sheet on a phone, typed dates, presets and clearing, and the keys.
/// </summary>
internal sealed partial class UiDatePickerBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Popup(),
            Phone(),
            Typed(),
            Presets(),
            Clear(),
            Keyboard()
        ];

    private static UiDocsTopic Popup() =>
        UiDocsTopic.Key("popup").Heading("A popup the browser opens").Paragraphs([
            "The calendar is in a popover, so the top layer, Escape, a click outside and the focus back on the button "
            + "come with it. A day closes it on the pick, a range on its second click, a preset at once. While it is open "
            + "the page behind it neither scrolls nor takes the pointer (`data-rask-lock`).",
            "It takes the calendar's steps (`Min`, `Max`, `Unavailable`, `MinRange`, `MaxRange`, `Months`, `OpenTo`, "
            + "`StartDay`, `WeekNumbers`, `SelectableHeader`, `WithToday`, `FixedWeeks`, `Locale`) and a field's. "
            + "`WithConfirmation()` writes nothing until \"Select date\". There is no multiple mode, as in Flux: several "
            + "days are the calendar's.",
        ]);

    private static UiDocsTopic Phone() =>
        UiDocsTopic.Key("phone").Heading("On a phone").Paragraphs([
            "Under 640 px the same popup is Flux's sheet, with nothing to write for it: centred over a page dimmed 10%, "
            + "one month whatever `Months` says, 44 px days, the presets a native select under the days, and Cancel and "
            + "Select date (Select range on a range) at its foot. A pick marks the day and writes nothing; the button "
            + "writes it, through the same binding. Cancel, Escape and a press on the dimmed page leave the value as it "
            + "was. The page hears which of the two it is from a box only the stylesheet sizes (`data-rask-measure`).",
        ]);

    private static UiDocsTopic Typed() =>
        UiDocsTopic.Key("typed").Heading("Typed dates").Paragraphs([
            "`Type(Ui.DatePickerType.Input)`, `Ui.DatePickerInput` and `WithInputs()` draw month, day and year as parts "
            + "in the locale's order, Flux's `custom`. `Ui.DatePickerInput.Native` and "
            + "`WithInputs(Ui.DatePickerInputVariant.Native)` draw the browser's own `<input type=\"date\">` in the same "
            + "box, Flux's `native`. Flux's docs default to native and call custom the recommended and future default; "
            + "its own pages draw custom, and so does the kit unless told.",
            "The typed trigger is one field in three parts: digits only, a part that can take no more moves on (3 is "
            + "March), the horizontal arrows walk the parts and the vertical ones step them, and a pasted date is shared "
            + "out. A whole date is written as soon as the year has four digits. Everything in the trigger but a part "
            + "opens the calendar. The parts are the runtime's `data-rask-segments`, and one hidden field carries "
            + "`yyyy-MM-dd` to the binding.",
            "Where it departs from Flux, by the owner's decision: Flux keeps the last whole date behind parts that no "
            + "longer show it. Here a half-filled date is no date: the field says \"Enter a valid date.\", the save is "
            + "refused, and with every part emptied the picker holds nothing.",
        ]);

    private static UiDocsTopic Presets() =>
        UiDocsTopic.Key("presets").Heading("Presets").Paragraphs([
            "`WithPresets()` lists Flux's default seven; `Presets([..])` lists the `Ui.DateRangePreset`s you name, in "
            + "your order. A preset writes `UiDateRange.Of(preset, today, startDay, Min)`, whose `Preset` names it, and "
            + "the button then shows the preset's label. All Time starts at `Min`. The presets are a radio group one "
            + "arrow walks, and the preset an arrow reaches is chosen, which closes the picker.",
        ]);

    private static UiDocsTopic Clear() =>
        UiDocsTopic.Key("clear").Heading("Clearing").Paragraphs([
            "`Clearable()`, on the picker or on a `Ui.DatePickerButton` or `Ui.DatePickerInput` in its `Trigger`, puts a "
            + "button in the trigger, before its chevron, while a date is chosen. A bound `DateOnly?` or `UiDateRange?` "
            + "becomes `null`, one that cannot be null goes back to its unset value, and a range is emptied whole. Flux "
            + "documents the prop and draws it on none of its pages, so the button is the time picker's: the same 32 px "
            + "cross, 30 px from the trigger's end.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The keyboard").Paragraphs([
            "As measured on Flux. Inside the popup the calendar's own keys apply.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("Enter, Space, ArrowDown, ArrowUp", "Open the closed button."),
                ("Escape, a click outside", "Close; the focus goes back to the button."),
                ("An arrow in the presets", "Chooses the next preset, which closes the picker."),
                ("Digits in a typed part", "Fill it, and move on once it can take no more."),
            ])
        ];
}
