using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// A month, with any number of days to pick.
/// </summary>
/// <remarks>
/// <para>
/// Not written by name: it is reached through <c>UiCalendar</c>, which becomes this control when the model says it
/// holds several days — <c>Ui.Calendar.Bind(() =&gt; model.DaysOff)</c> over a <c>List&lt;DateOnly&gt;</c>,
/// <c>HashSet&lt;DateOnly&gt;</c> or any other collection, or <c>Ui.Calendar.Values([...])</c> for the parent to
/// own. The controlled opening is <c>Values</c> rather than <c>Value</c> for the reason it is on <c>UiSelect</c>: a
/// collection expression and a bare <c>null</c> fit every collection shape, and the single day's opening, equally.
/// </para>
/// <para>
/// A click on a day adds it, a click on a chosen day takes it back out, and the days are written back in date order
/// whatever order they were clicked in.
/// </para>
/// </remarks>
[RaskChainEntry("UiCalendar")]
public sealed partial class UiCalendarMultiple : Component, IFormControl<ICollection<DateOnly>>
{
    private DateOnly? _month;

    /// <summary>The accessible name — what the days are for.</summary>
    public required string Label { get; set; }

    /// <inheritdoc cref="UiCalendar.Month" />
    public DateOnly? Month { get; set; }

    /// <inheritdoc cref="UiCalendar.OnMonth" />
    public Callback<DateOnly> OnMonth { get; set; }

    /// <inheritdoc cref="UiCalendar.Min" />
    public DateOnly? Min { get; set; }

    /// <inheritdoc cref="UiCalendar.Max" />
    public DateOnly? Max { get; set; }

    /// <inheritdoc cref="UiCalendar.FirstDay" />
    public DayOfWeek? FirstDay { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    /// <remarks>The chosen days. Null or empty is nothing chosen.</remarks>
    public ICollection<DateOnly>? Value { get; set; }

    /// <inheritdoc />
    public Callback<ICollection<DateOnly>> OnChange { get; set; }

    /// <inheritdoc />
    public Expression<Func<ICollection<DateOnly>>>? Bind { get; set; }

    /// <inheritdoc />
    public Validator<ICollection<DateOnly>>? Validate { get; set; }

    /// <inheritdoc />
    public Callback<ICollection<DateOnly>> AfterBind { get; set; }

    // The view month is a FIELD when the page leaves it unset, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var (acc, ctx, current) = UiFormCommit.Resolve<ICollection<DateOnly>>(this);
        var chosen = current ?? [];

        return UiDayGrid.Render(new UiDayGrid.View(
            Label,
            UiDayGrid.MonthOf(Month, _month, chosen.Count > 0 ? chosen.Min() : null),
            FirstDay,
            Min,
            Max,
            Class,
            PageAsync,
            date => new UiDayGrid.DayState(chosen.Contains(date), false),
            date => UiFormCommit.CommitSelectionAsync(this, acc, ctx, Toggle(chosen, date)),
            static _ => null));
    }

    internal static List<DateOnly> Toggle(ICollection<DateOnly> chosen, DateOnly date)
    {
        var next = chosen.Where(d => d != date).ToList();
        if (next.Count == chosen.Count)
        {
            next.Add(date);
        }

        next.Sort();
        return next;
    }

    private Task PageAsync(DateOnly month)
    {
        _month = month;
        return OnMonth.Invoke(month).AsTask();
    }
}
