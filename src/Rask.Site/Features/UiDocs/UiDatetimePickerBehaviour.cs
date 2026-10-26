namespace Rask.Site.Features.UiDocs;

/// <summary>How a datetime picker behaves: what it binds, when it writes, time zones, a phone, and the keys.</summary>
internal sealed partial class UiDatetimePickerBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Value(),
            Writes(),
            Zones(),
            Phone(),
            Keyboard()
        ];

    private static UiDocsTopic Value() =>
        UiDocsTopic.Key("value").Heading("What it binds").Paragraphs([
            "`Ui.DatetimePicker` binds a `DateTime` or a `DateTime?`, `Ui.DatetimePicker.Range` a `UiDateTimeRange` — Flux's "
            + "`DateTimeRange`: a `Start` and an `End`, `HasStart` and `HasEnd`, written in an address as "
            + "`2026-10-14T22:00..2026-10-15T02:00`. Precise to the minute: seconds are dropped. A bound value is written "
            + "with the reader's next action, at once with `.Live()`.",
            "`Unavailable` takes `UiDatetimeSlot`s, which a day (`DateOnly`), a stretch of every day (`TimeOnly`, "
            + "`UiTimeRange`) and a time of one day (`DateTime`, `UiDatetimeSlot.On(day, from, to)`) each convert to, as "
            + "Flux mixes them in one list. `OpenTo` takes a moment, a day or only a time.",
        ]);

    private static UiDocsTopic Writes() =>
        UiDocsTopic.Key("writes").Heading("When it writes").Paragraphs([
            "Picking a time closes the picker and writes the moment; changing only the day writes it at once and leaves "
            + "the popup open. A day alone or a time alone is held until the other is there, and `DefaultTime` fills the "
            + "time in as soon as a day is picked. A range, typed times and `WithConfirmation()` wait for Select instead; "
            + "Cancel, Escape and a press outside drop what was held. A range whose end is not after its start says so "
            + "and cannot be selected.",
            "All of it is the browser's, as Flux's script keeps it: paging, the arrows, the times a day rules out, what the "
            + "button says. The page hears the value, and nothing else.",
        ]);

    private static UiDocsTopic Zones() =>
        UiDocsTopic.Key("timezones").Heading("Time zones").Paragraphs([
            "Wall-clock times, as a `datetime-local` field holds them: `2026-10-14T10:30` is half past ten that day and is "
            + "not converted between zones. For readers in other zones, convert to and from theirs yourself — "
            + "`TimeZoneInfo.ConvertTimeToUtc(startsAt, reader)` on the way in. `Min` and `Max` are moments of the server's "
            + "clock: Flux's `min=\"now\"` is `.Min(DateTime.Now)` of the clock you trust.",
        ]);

    private static UiDocsTopic Phone() =>
        UiDocsTopic.Key("phone").Heading("On a phone").Paragraphs([
            "Under 640 px it is the date picker's sheet: one month, the time a field under it with its own list, and "
            + "Cancel and Select at its foot. Nothing is written until Select.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The keyboard").Paragraphs([
            "The calendar's keys are the date picker's. Tab goes from the month steps to the day, then to the time "
            + "the column opens on.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("ArrowDown, ArrowUp", "In the column of times: the next or the previous time that can be picked, stopping at the ends."),
                ("Home, End", "In the column of times: the first or the last."),
                ("Enter, Space", "Picks the time in focus."),
                ("Escape", "Closes, and drops what was held."),
            ])
        ];
}
