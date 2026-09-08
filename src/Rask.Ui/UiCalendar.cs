namespace Rask.Ui;

/// <summary>
/// A month, with a day to pick.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built here in C#, not from a web component.</b> daisyUI styles the third-party <c>cally</c>
/// element, which is JavaScript this kit does not ship — so the grid is laid out in C#, over daisyUI's
/// <c>calendar</c> classes, and moving between months is an ordinary re-render.
/// </para>
/// <para>
/// The days are <c>&lt;button&gt;</c> elements in a table, so a keyboard reaches every one and a screen
/// reader gets the column headers with them. Each carries its full date as its accessible name: "14"
/// on its own is not something you can act on when the month has scrolled out of earshot.
/// </para>
/// <para>
/// It has no text field of its own. Pair it with one where a date can also be typed — typing is faster
/// than paging through months for anything more than a few weeks away, and it is the only route for
/// somebody who cannot use a pointer comfortably.
/// </para>
/// </remarks>
public sealed partial class UiCalendar : Component
{
    /// <summary>The accessible name — what the date is for.</summary>
    public required string Label { get; set; }

    /// <summary>Any day in the month being shown. Defaults to the month of the selected day, or today.</summary>
    public DateOnly? Month { get; set; }

    /// <summary>Runs with the first day of the month the reader asked for.</summary>
    public Action<DateOnly>? OnMonth { get; set; }

    /// <summary>The chosen day.</summary>
    public DateOnly? Selected { get; set; }

    /// <summary>Runs with the day the reader picked.</summary>
    public Action<DateOnly>? OnSelect { get; set; }

    /// <summary>The earliest selectable day. Days before it are disabled rather than hidden.</summary>
    public DateOnly? Min { get; set; }

    /// <summary>The latest selectable day.</summary>
    public DateOnly? Max { get; set; }

    /// <summary>Which day the week starts on. Defaults to Monday.</summary>
    public DayOfWeek? FirstDay { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var shown = Month ?? Selected ?? DateOnly.FromDateTime(DateTime.Today);
        var first = new DateOnly(shown.Year, shown.Month, 1);
        var firstDay = FirstDay ?? DayOfWeek.Monday;

        // How many blanks before the 1st. The +7 keeps it non-negative whichever day the week starts on.
        var lead = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
        var days = DateTime.DaysInMonth(first.Year, first.Month);

        return Div
            .Role("group")
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(UiClass.Compose("calendar rounded-box border border-base-300 bg-base-100 p-3", Class))[
            Div.Class("mb-2 flex items-center justify-between gap-2")[
                MonthStep("prev", first.AddMonths(-1), "Previous month", UiIconName.ArrowLeft),
                Div.Class("text-sm font-semibold")[
                    first.ToString("MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture)
                ],
                MonthStep("next", first.AddMonths(1), "Next month", UiIconName.ArrowRight)
            ],
            Table.Class("calendar-month w-full")[
                Thead[
                    Tr[
                        Enumerable.Range(0, 7).Select(i =>
                        {
                            var day = (DayOfWeek)(((int)firstDay + i) % 7);
                            var name = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat
                                .GetShortestDayName(day);

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
                                    : Td.Key(slot)[Day(new DateOnly(first.Year, first.Month, number))];
                            })
                        ])
                ]
            ]
        ];
    }

    private Component MonthStep(object key, DateOnly target, string label, UiIconName icon)
    {
        var button = Button
            .Key(key)
            .Type("button")
            .Class("btn btn-ghost btn-sm btn-square")
            .Aria(new Dictionary<string, string?> { ["label"] = label });

        if (OnMonth is { } onMonth)
        {
            button = button.OnClick(() => onMonth(target));
        }

        return button[UiIcon.Name(icon).Class("size-4 shrink-0")];
    }

    private Component Day(DateOnly date)
    {
        var chosen = Selected == date;
        var blocked = (Min is { } min && date < min) || (Max is { } max && date > max);

        var button = Button
            .Key(date.Day)
            .Type("button")
            .Class(UiClass.Compose("btn btn-ghost btn-sm btn-square", chosen ? "btn-active" : ""))
            .Disabled(blocked)
            .Aria(new Dictionary<string, string?>
            {
                // The full date, not the number: "14" is not something you can act on once the month
                // has scrolled out of earshot.
                ["label"] = date.ToString("D", System.Globalization.CultureInfo.CurrentCulture),
                ["pressed"] = chosen ? "true" : "false",
            });

        if (OnSelect is { } onSelect && !blocked)
        {
            button = button.OnClick(() => onSelect(date));
        }

        return button[date.Day.ToString(System.Globalization.CultureInfo.CurrentCulture)];
    }
}
