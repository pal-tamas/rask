using System.Globalization;

namespace Rask;

/// <summary>
///     A datetime picker's times as the page first draws them: the column beside the calendar, and the time fields
///     under it — Flux's own time pickers, one for a moment and one for each end of a range.
/// </summary>
/// <remarks>
///     Everything a reader does to them is the browser's (<c>rask-calendar.ts</c>): which time is chosen, which cannot
///     be picked on the day chosen, the field's words. Nothing here carries a handler; the page hears the value.
/// </remarks>
internal abstract partial class UiDatetimePickerTimes : global::Rask.Core.RaskMarkup
{
    private static readonly UiPartMarker TimesMarker = new("ui-datetime-picker-times");

    // The time picker's list marks but the page lock: the popup it opens in already holds the page, and a second lock
    // released inside the first would let the page go back to its top.
    private static readonly Dictionary<string, string?> ListMarks = new(StringComparer.Ordinal)
    {
        ["rask-place"] = "bottom start 5 0",
        ["rask-open-mark"] = "",
    };

    /// <summary>The column of times beside the calendar: a desktop's way to pick a moment's time from the list.</summary>
    /// <param name="times">The times, resolved.</param>
    /// <param name="day">The chosen day, which rules some times out.</param>
    /// <param name="chosen">The chosen time.</param>
    internal static global::Rask.Core.Component Column(UiDatetimeTimes times, DateOnly? day, TimeOnly? chosen)
    {
        var label = RaskStrings.Get(RaskString.DatetimePickerTime, "Time");
        var stop = times.Start(chosen, day);
        var top = Math.Max(0, (stop is { } at ? times.Listed.ToList().IndexOf(at) : 0) - 2);

        return Div.Class(UiDatetimePickerLook.Column)[
            Div.Class(UiDatetimePickerLook.ColumnHeading)[
                Ui.Icon.Name(Ui.IconName.Clock).Mini.Class(UiDatetimePickerLook.ColumnIcon),
                Span.Class(UiDatetimePickerLook.ColumnTitle)[label]
            ],
            Div.Class(UiDatetimePickerLook.ColumnScroll)[
                Div.Class(UiDatetimePickerLook.Times)
                    .Data(TimesMarker.With(null))
                    .Role("listbox")
                    .Aria("label", label)
                    .Attributes(UiCalendarHook.Named("times"), (UiCalendarHook.Slot, "0"))[
                    times.Listed.Select((time, index) => ColumnRow(times, index, time, time == chosen, time == stop, times.Off(day, time), index == top))
                ]
            ]
        ];
    }

    // One time of the column. The list opens scrolled to the row it starts on, two rows down from its top, with no
    // script — the platform's answer — and the runtime puts it there again at every opening (rask-calendar.ts).
    private static global::Rask.Core.Component ColumnRow(UiDatetimeTimes times, int index, TimeOnly time, bool selected, bool stop, bool off, bool top)
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["time"] = time.ToString("HH:mm", CultureInfo.InvariantCulture),
        };
        if (selected)
        {
            data["selected"] = "";
        }

        if (stop)
        {
            data["rask-time-start"] = "";
        }

        return Button
            .Key(index)
            .Id(UiSelectNav.OptId(times.ListId(0), index))
            .Type(ButtonType.Button)
            .Role("option")
            .TabIndex(stop ? 0 : -1)
            .Class(top ? UiDatetimePickerLook.TimeScrolledTo : UiDatetimePickerLook.Time)
            .Disabled(off)
            .Data(data)
            .Aria("selected", selected ? "true" : "false")[
            Span.Class(UiDatetimePickerLook.TimeCheck)[
                Ui.Icon.Name(Ui.IconName.Check).Micro.Class(UiDatetimePickerLook.TimeCheckIcon)
            ],
            Span.Class(UiDatetimePickerLook.TimeText).Attributes(("dir", "ltr"))[times.Written(time)]
        ];
    }

    /// <summary>
    ///     The time field of one end — <paramref name="slot" /> 0 for a moment or a range's start, 1 for its end —
    ///     as Flux draws it: a time picker's button and list, or its typed hour and minute with the list beside them.
    /// </summary>
    /// <param name="times">The times, resolved.</param>
    /// <param name="slot">Which end.</param>
    /// <param name="name">Flux's name for it: <c>single</c>, <c>start</c> or <c>end</c>.</param>
    /// <param name="day">The end's chosen day, which rules some times out.</param>
    /// <param name="chosen">The end's chosen time.</param>
    internal static global::Rask.Core.Component Field(UiDatetimeTimes times, int slot, string name, DateOnly? day, TimeOnly? chosen)
    {
        var list = times.ListId(slot + 1);
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ui-datetime-picker-time"] = name,
            ["ui-control"] = "",
            ["ui-time-picker"] = "",
            // The list's cursor, walked in the browser (rask-time-list.ts).
            ["rask-time-list"] = "",
        };

        return Div.Class(UiDatetimePickerLook.Picker)
            .Data(marks)
            .Attributes(UiCalendarHook.Named("time"), (UiCalendarHook.Slot, Invariant(slot)))[
            Div.Key("trigger").Class("inline")[times.Typed ? Typed(times, list, chosen) : Pressed(times, list, chosen)],
            List(times, list, slot, day, chosen)
        ];
    }

    // The button that shows the end's time and opens its list.
    private static global::Rask.Core.Component Pressed(UiDatetimeTimes times, string list, TimeOnly? chosen) =>
        Button
            .Type(ButtonType.Button)
            .Role("combobox")
            .Class(UiTimePickerLook.Button, UiTimePickerLook.ButtonSize(Ui.TimePickerSize.Base))
            .Disabled(times.Disabled)
            .Data(UiTimePickerLook.ButtonMarks)
            .Aria(new Dictionary<string, string?>(StringComparer.Ordinal) { ["controls"] = list, ["haspopup"] = "listbox", ["expanded"] = "false" })
            .Attributes(("popovertarget", list), ("data-rask-listbox-button", null), ("data-rask-open-mark", list), ("style", "anchor-name:--" + list))[
            Ui.Icon.Name(Ui.IconName.Clock).Mini.Class(UiTimePickerLook.ButtonIcon),
            Div.Class(UiTimePickerLook.Selected).Attributes(UiCalendarHook.Named("time-text"))[
                chosen is { } time
                    ? Div.Key("chosen").Class(UiDatetimePickerLook.TimeWords).Attributes(("dir", "auto"))[times.Written(time)]
                    : Span.Key("placeholder").Class(UiTimePickerLook.Placeholder).Data("ui-time-picker-placeholder", null)[RaskStrings.Get(RaskString.DatetimePickerTime, "Time")]
            ],
            Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiTimePickerLook.ButtonChevron)
        ];

    // The hour and minute typed as one field (rask-segments.ts), its whole time in the one hidden field the
    // calendar reads; anything in the box but a part opens the list of suggestions.
    private static global::Rask.Core.Component Typed(UiDatetimeTimes times, string list, TimeOnly? chosen) =>
        Div.Class(UiTimePickerLook.InputBox, UiTimePickerLook.BoxSize(Ui.TimePickerSize.Base))
            .Data(UiTimePickerLook.BoxMarks)
            .Attributes(("data-rask-toggle", list), ("style", "anchor-name:--" + list))[
            Ui.Icon.Name(Ui.IconName.Clock).Mini.Class(UiTimePickerLook.InputIcon),
            Div.Class(UiTimePickerLook.Segments).Data(UiTimePickerLook.SegmentsMark).Attributes(("dir", "ltr"))[
                UiTimeParts.Of(times.Twelve, times.Culture, times.Disabled, look: UiDatetimePickerLook.Segment, period: UiDatetimePickerLook.SegmentMeridiem,
                    // Flux keeps a space's width between the minute and AM/PM.
                    space: "\u00A0"),
                Input.Value(chosen?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty).Type(InputType.Hidden).Attributes(UiRuntimeMarks.NotFormData)
            ],
            Span.Class("grow"),
            Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiTimePickerLook.InputChevron)
        ];

    // The end's list of times, in the top layer under its field. A press on a row closes it; what the press chooses
    // is the browser's to hear.
    private static global::Rask.Core.Component List(UiDatetimeTimes times, string list, int slot, DateOnly? day, TimeOnly? chosen)
    {
        var start = times.Start(chosen, day);
        return Div
            .Key("list")
            .Id(list)
            .Popover(Popover.Auto)
            .TabIndex(-1)
            .Class(UiTimePickerLook.Options)
            .Role("listbox")
            .Aria("multiselectable", "false")
            .Data(ListMarks)
            .Attributes(
                UiCalendarHook.Named("time-list"),
                (UiCalendarHook.Slot, Invariant(slot)),
                ("style", "position-anchor:--" + list
                          + ";inset:auto;top:calc(anchor(bottom) + 5px);inset-inline-start:anchor(start)"
                          + ";width:anchor-size(width);position-try-fallbacks:flip-block"))[
            times.Listed.Select((time, index) =>
            {
                var off = times.Off(day, time);
                var row = UiTimeParts.Row(list, index, time, time == chosen, time == start, off);
                return row.Attributes(("popovertarget", list), ("popovertargetaction", "hide"))[
                    UiTimeParts.RowContent(times.Written(time), time == chosen)
                ];
            })
        ];
    }

    private static string Invariant(int number) => number.ToString(CultureInfo.InvariantCulture);
}
