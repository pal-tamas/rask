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
/// through months changes nothing a form would submit. Leave <see cref="Month" /> unset and the calendar
/// keeps track of the month it is showing itself.
/// </para>
/// <para>
/// <b>The same entry picks several days or a range.</b> Bind a collection of days — <c>List&lt;DateOnly&gt;</c>,
/// <c>HashSet&lt;DateOnly&gt;</c> and the rest, or <c>.Values([...])</c> — and it is
/// <see cref="UiCalendarMultiple" />; bind a <see cref="UiDateRange" />, or <c>.Value(new UiDateRange(a, b))</c>,
/// and it is <see cref="UiCalendarRange" />. The model says which, so there is no mode to set.
/// </para>
/// <para>
/// It has no text field of its own. Pair it with one where a date can also be typed — typing is faster
/// than paging through months for anything more than a few weeks away, and it is the only route for
/// somebody who cannot use a pointer comfortably.
/// </para>
/// </remarks>
public sealed partial class UiCalendar : Component, IFormControl<DateOnly>
{
    private DateOnly? _month;

    /// <summary>The accessible name — what the date is for.</summary>
    public required string Label { get; set; }

    /// <summary>
    ///     Any day in the month being shown. Leave it unset and the calendar opens on the month of the chosen day,
    ///     or today, and pages by itself; set it to own the view, and pair it with <see cref="OnMonth" />.
    /// </summary>
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

    // The view month is a FIELD when the page leaves it unset, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var (acc, ctx, chosen) = UiFormCommit.Resolve<DateOnly>(this);

        return UiDayGrid.Render(new UiDayGrid.View(
            Label,
            UiDayGrid.MonthOf(Month, _month, chosen),
            FirstDay,
            Min,
            Max,
            Class,
            PageAsync,
            date => new UiDayGrid.DayState(chosen == date, false),
            date => UiFormCommit.CommitAsync(this, acc, ctx, date),
            static _ => null));
    }

    private Task PageAsync(DateOnly month)
    {
        _month = month;
        return OnMonth?.Invoke(month) ?? Task.CompletedTask;
    }
}
