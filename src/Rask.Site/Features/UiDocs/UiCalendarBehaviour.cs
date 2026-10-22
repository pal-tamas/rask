namespace Rask.Site.Features.UiDocs;

/// <summary>How a calendar behaves: the mode as the opening step, the range, the keyboard, and on a phone.</summary>
internal sealed partial class UiCalendarBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Mode(),
            Range(),
            Keyboard(),
            Phone()
        ];

    private static UiDocsTopic Mode() =>
        UiDocsTopic.Key("mode").Heading("The mode is the opening step").Paragraphs([
            "No step is Flux's default, one day: `Ui.Calendar.Bind(() => m.Delivery)` binds a `DateOnly` or a "
            + "`DateOnly?`. `Ui.Calendar.Multiple` binds a collection of days and `Ui.Calendar.Range` a `UiDateRange`, so "
            + "a range bound to a single day does not compile. A mode given as a value, `Mode(Ui.CalendarMode.Range)`, has "
            + "to agree with what is bound; when it does not, rendering throws and names both.",
            "A second click on the chosen day clears it: `default(DateOnly)`, or `null` through a nullable binding. "
            + "`Min` and `Max` disable the days outside them, and a month step too once the whole month that way is out "
            + "of reach.",
        ]);

    private static UiDocsTopic Range() =>
        UiDocsTopic.Key("range").Heading("A range is always whole").Paragraphs([
            "`UiDateRange(Start, End)` is written only once it has both ends. The first click is held by the calendar and "
            + "drawn as the start, the stretch to the day under the pointer or the keyboard is drawn as it would be, and "
            + "the second click, on that day or a later one, writes the range; a click before the start begins again "
            + "from there. `Count`, `Contains(day)` and `Between(a, b)` read it, and `default(UiDateRange)` is nothing "
            + "chosen. Flux's `DateRange` presets are `UiDateRange.Of(preset, today, …)`, shown on the date picker's page.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The keyboard").Paragraphs([
            "One day is in the Tab order: the chosen one, else today, else the 1st. The keys and the focus are the "
            + "runtime's (`data-rask-contain-keys` and `data-rask-focus-follows` on the grid, `data-rask-focus-target` on "
            + "the tab stop), and none of them scrolls the page behind the grid.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("ArrowLeft, ArrowRight", "A day back or on, stepping over disabled days and into the next month; the focus goes with the day."),
                ("ArrowUp, ArrowDown", "A week back or on."),
                ("PageUp, PageDown, Home, End", "Page a month and let the focus fall to the page, as Flux's do."),
                ("Enter, Space", "Pick the day."),
            ])
        ];

    private static UiDocsTopic Phone() =>
        UiDocsTopic.Key("phone").Heading("On a phone").Paragraphs([
            "Under 640 px the calendar folds to one month whatever `Months` says, and its days grow to 44 px a thumb can "
            + "press. Each cell is a `gridcell` with `aria-selected`, and its button carries the full date as its name "
            + "(\"Thursday, January 15, 2026\").",
        ]);
}
