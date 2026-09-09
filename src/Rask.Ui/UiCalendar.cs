using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

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
/// A form control over a <c>DateOnly</c>, concretely rather than generically — a day grid picks a day.
/// <c>.Bind(() =&gt; model.Delivery)</c> two-way binds and drives the surrounding <c>Form</c>'s
/// validation; <see cref="Value" /> with <see cref="OnChange" /> leaves it with the parent. Note the
/// pair that is NOT the value: <see cref="Month" /> and <see cref="OnMonth" /> are the view, and paging
/// through months changes nothing a form would submit.
/// </para>
/// <para>
/// It has no text field of its own. Pair it with one where a date can also be typed — typing is faster
/// than paging through months for anything more than a few weeks away, and it is the only route for
/// somebody who cannot use a pointer comfortably.
/// </para>
/// </remarks>
public sealed partial class UiCalendar : Component, IFormControl<DateOnly>
{
    /// <summary>The accessible name — what the date is for.</summary>
    public required string Label { get; set; }

    /// <summary>Any day in the month being shown. Defaults to the month of the chosen day, or today.</summary>
    public DateOnly? Month { get; set; }

    /// <summary>Runs with the first day of the month the reader asked for.</summary>
    public Callback<DateOnly>? OnMonth { get; set; }

    /// <summary>The earliest selectable day. Days before it are disabled rather than hidden.</summary>
    public DateOnly? Min { get; set; }

    /// <summary>The latest selectable day.</summary>
    public DateOnly? Max { get; set; }

    /// <summary>Which day the week starts on. Defaults to Monday.</summary>
    public DayOfWeek? FirstDay { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     The chosen day. Not nullable — see <see cref="UiCheckbox.Value" /> — so "nothing chosen" is
    ///     <c>default(DateOnly)</c>, which is 1 January year 1 and lands in no month a reader will ever
    ///     page to. Bind a <c>DateOnly?</c> where the difference between unset and a real date matters.
    /// </remarks>
    public DateOnly Value { get; set; }

    /// <inheritdoc />
    public Callback<DateOnly>? OnChange { get; set; }


    /// <inheritdoc />
    public Expression<Func<DateOnly>>? Bind { get; set; }

    /// <inheritdoc />
    public Validator<DateOnly>? Validate { get; set; }


    /// <inheritdoc />
    public Callback<DateOnly>? AfterBind { get; set; }


    /// <inheritdoc />
    protected override Component? Render()
    {
        var (acc, ctx, chosen) = UiFormCommit.Resolve<DateOnly>(this);

        // default(DateOnly) is year 1, which is not a month anybody meant to look at, so it does not get
        // to choose the view the way a real chosen day does.
        var shown = Month ?? (chosen == default ? DateOnly.FromDateTime(DateTime.Today) : chosen);
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
                    first.ToString("MMMM yyyy", CultureInfo.CurrentCulture)
                ],
                MonthStep("next", first.AddMonths(1), "Next month", UiIconName.ArrowRight)
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
                                    : Td.Key(slot)[
                                        Day(new DateOnly(first.Year, first.Month, number), chosen, acc, ctx)
                                    ];
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
            button = button.OnClick(() => onMonth.Invoke(target) ?? Task.CompletedTask);
        }

        return button[UiIcon.Name(icon).Class("size-4 shrink-0")];
    }

    private Component Day(
        DateOnly date,
        DateOnly chosen,
        ExpressionAccessor.Accessor? accessor,
        EditContext? context)
    {
        var picked = chosen == date;
        var blocked = (Min is { } min && date < min) || (Max is { } max && date > max);

        var button = Button
            .Key(date.Day)
            .Type("button")
            .Class(UiClass.Compose("btn btn-ghost btn-sm btn-square", picked ? "btn-active" : ""))
            .Disabled(blocked)
            .Aria(new Dictionary<string, string?>
            {
                // The full date, not the number: "14" is not something you can act on once the month
                // has scrolled out of earshot.
                ["label"] = date.ToString("D", CultureInfo.CurrentCulture),
                ["pressed"] = picked ? "true" : "false",
            });

        if (!blocked)
        {
            button = button.OnClick(() => UiFormCommit.CommitAsync(this, accessor, context, date));
        }

        return button[date.Day.ToString(CultureInfo.CurrentCulture)];
    }
}
