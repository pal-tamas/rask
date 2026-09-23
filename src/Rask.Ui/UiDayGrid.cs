using System.Globalization;

namespace Rask;

/// <summary>
/// The month grid every calendar in the kit draws: the header with its two month steps, the weekday row, and a
/// button per day.
/// </summary>
/// <remarks>
/// <para>
/// Shared by <see cref="UiCalendar" />, <see cref="UiCalendarMultiple" />, <see cref="UiCalendarRange" /> and the
/// three date pickers, which differ only in what "chosen" means for a day and what a click on one does. Six copies
/// of the lead-in arithmetic would be six chances to get a month that starts on a Sunday wrong.
/// </para>
/// <para>
/// A markup host rather than a component: it has no state of its own and no lifecycle, it only names markup — the
/// state (which day, which month) belongs to the control that calls it.
/// </para>
/// </remarks>
internal abstract partial class UiDayGrid : global::Rask.Core.RaskMarkup
{
    /// <summary>What one day looks like: chosen, inside a chosen range, or neither.</summary>
    internal readonly record struct DayState(bool Picked, bool Between);

    /// <summary>Everything the grid needs from the control drawing it.</summary>
    internal sealed record View(
        string Label,
        DateOnly Shown,
        DayOfWeek? FirstDay,
        DateOnly? Min,
        DateOnly? Max,
        string? Class,
        Func<DateOnly, Task>? OnMonth,
        Func<DateOnly, DayState> State,
        Func<DateOnly, Task> Pick,
        // The popover a pick on this day closes, when the grid sits in a picker and this pick finishes the choice.
        Func<DateOnly, string?> Closes);

    /// <summary>The first day of the month a control should show, given what it was told and what is chosen.</summary>
    internal static DateOnly MonthOf(DateOnly? month, DateOnly? viewed, DateOnly? chosen)
    {
        // default(DateOnly) is year 1, which is not a month anybody meant to look at, so it does not get to choose
        // the view the way a real chosen day does.
        var shown = month ?? viewed ?? (chosen is { } c && c != default ? c : DateOnly.FromDateTime(DateTime.Today));
        return new DateOnly(shown.Year, shown.Month, 1);
    }

    internal static global::Rask.Core.Component Render(View view)
    {
        var first = new DateOnly(view.Shown.Year, view.Shown.Month, 1);
        var firstDay = view.FirstDay ?? DayOfWeek.Monday;

        // How many blanks before the 1st. The +7 keeps it non-negative whichever day the week starts on.
        var lead = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
        var days = DateTime.DaysInMonth(first.Year, first.Month);

        return Div
            .Role("group")
            .Aria(new Dictionary<string, string?> { ["label"] = view.Label })
            .Class(UiClass.Compose("calendar rounded-box border border-base-300 bg-base-100 p-3", view.Class))[
            Div.Class("mb-2 flex items-center justify-between gap-2")[
                MonthStep(view, "prev", first.AddMonths(-1), "Previous month", Ui.IconName.ArrowLeft),
                Div.Class("text-sm font-semibold")[
                    first.ToString("MMMM yyyy", CultureInfo.CurrentCulture)
                ],
                MonthStep(view, "next", first.AddMonths(1), "Next month", Ui.IconName.ArrowRight)
            ],
            Table.Class("calendar-month w-full")[
                Thead[
                    Tr[
                        Enumerable.Range(0, 7).Select(i =>
                        {
                            var day = (DayOfWeek)(((int)firstDay + i) % 7);
                            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(day);

                            return Th.Key(day).Class("text-xs font-normal opacity-60")[name];
                        })
                    ]
                ],
                Tbody[
                    Enumerable.Range(0, (lead + days + 6) / 7).Select(week =>
                        Tr.Key(week)[
                            Enumerable.Range(0, 7).Select(slot =>
                            {
                                var number = (week * 7) + slot - lead + 1;

                                return number < 1 || number > days
                                    ? Td.Key(slot)
                                    : Td.Key(slot)[DayButton(view, new DateOnly(first.Year, first.Month, number))];
                            })
                        ])
                ]
            ]
        ];
    }

    private static global::Rask.Core.Component MonthStep(
        View view, object key, DateOnly target, string label, Ui.IconName icon)
    {
        var button = Button
            .Key(key)
            .Type("button")
            .Class("btn btn-ghost btn-sm btn-square")
            .Aria(new Dictionary<string, string?> { ["label"] = label });

        if (view.OnMonth is { } onMonth)
        {
            button = button.OnClick(() => onMonth(target));
        }

        return button[Ui.Icon.Name(icon).Class("size-4 shrink-0")];
    }

    private static global::Rask.Core.Component DayButton(View view, DateOnly date)
    {
        var state = view.State(date);
        var blocked = (view.Min is { } min && date < min) || (view.Max is { } max && date > max);

        var button = Button
            .Key(date.Day)
            .Type("button")
            .Class(UiClass.Compose(
                "btn btn-ghost btn-sm btn-square",
                state.Picked ? "btn-active" : "",
                // Inside a range but not one of its ends: tinted, so the stretch reads as one span.
                state.Between && !state.Picked ? "bg-base-200" : ""))
            .Disabled(blocked)
            .Aria(new Dictionary<string, string?>
            {
                // The full date, not the number: "14" is not something you can act on once the month has
                // scrolled out of earshot.
                ["label"] = date.ToString("D", CultureInfo.CurrentCulture),
                ["pressed"] = state.Picked ? "true" : "false",
            });

        if (!blocked)
        {
            button = button.OnClick(() => view.Pick(date));
            if (view.Closes(date) is { } panel)
            {
                // The browser closes the picker's popover on the same click that picks the day, as a chosen option
                // closes Ui.Select's list — no runtime, and the C# handler still runs.
                button = button.Attributes(("popovertarget", panel), ("popovertargetaction", "hide"));
            }
        }

        return button[date.Day.ToString(CultureInfo.CurrentCulture)];
    }

    /// <summary>What a date picker's button and popover need from the control drawing them.</summary>
    internal sealed record Picker(
        string Prefix,
        string FieldId,
        string? Text,
        string Placeholder,
        Dictionary<string, string?> Aria,
        bool Open,
        bool Disabled,
        string BoxClass,
        string? PanelName,
        Action<bool> OnToggle);

    /// <summary>The popover a picker's grid sits in.</summary>
    internal static string PanelIdOf(string prefix) => prefix + "-panel";

    /// <summary>
    ///     A date picker: a field-shaped button that shows the choice, and the grid in a popover beside it.
    /// </summary>
    /// <remarks>
    ///     The same popover machinery as <see cref="UiSelect{T}" />'s drawn list, so the browser owns opening it, the
    ///     top layer, Escape, a click outside and handing focus back to the button. It is a <c>dialog</c> rather than
    ///     a listbox: what is inside is a grid of buttons with its own month steps, reached with Tab.
    /// </remarks>
    internal static global::Rask.Core.Component PickerShell(Picker picker, global::Rask.Core.Component grid)
    {
        var panelId = PanelIdOf(picker.Prefix);
        var aria = new Dictionary<string, string?>(picker.Aria, StringComparer.Ordinal)
        {
            ["haspopup"] = "dialog",
            ["expanded"] = picker.Open ? "true" : "false",
            ["controls"] = panelId,
        };

        var panel = Div
            .Id(panelId)
            .Popover("auto")
            .Role("dialog")
            .Class("rounded-box shadow-sm")
            .Attributes(("style", "position-anchor:--" + picker.Prefix
                                  + ";position-area:block-end span-inline-end"
                                  + ";position-try-fallbacks:flip-block,flip-inline;margin:4px 0"))
            .OnToggle(e => picker.OnToggle(e.IsOpen));
        if (picker.PanelName is { } name)
        {
            panel = panel.Aria(new Dictionary<string, string?> { ["label"] = name });
        }

        return Div.Class("relative w-full")[
            Button
                .Id(picker.FieldId)
                .Type("button")
                .Class(UiClass.Compose(picker.BoxClass, "justify-between gap-2 text-left"))
                .Disabled(picker.Disabled)
                .Aria(aria)
                .Attributes(("popovertarget", panelId), ("style", "anchor-name:--" + picker.Prefix))[
                Span.Class(picker.Text is null ? "truncate opacity-60" : "truncate")[picker.Text ?? picker.Placeholder],
                Ui.Icon.Name(Ui.IconName.Calendar).Class("size-4 shrink-0 opacity-60")
            ],
            panel[grid]
        ];
    }

    /// <summary>The field-shaped box a picker's button is drawn as, in the input's tone, size and variant.</summary>
    internal static string BoxClass(Ui.Tone? tone, Ui.Size? size, Ui.Variant? variant) =>
        UiClass.Compose(
            "input validator w-full cursor-pointer",
            tone is { } t ? UiClassNames.InputTone(t) : "",
            size is { } s ? UiClassNames.InputSize(s) : "",
            variant is { } v ? UiClassNames.InputVariant(v) : "");

    /// <summary>A day as a picker's button shows it: the platform's short date, in the reader's culture.</summary>
    internal static string Short(DateOnly date) => date.ToString("d", CultureInfo.CurrentCulture);
}
