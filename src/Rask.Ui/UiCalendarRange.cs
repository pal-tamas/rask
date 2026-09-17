using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A month, with a range of days to pick: a first click for the start and a second for the end.
/// </summary>
/// <remarks>
/// <para>
/// Not written by name: it is reached through <c>UiCalendar</c>, which becomes this control when the model holds a
/// <see cref="UiDateRange" /> — <c>UiCalendar.Bind(() =&gt; model.Stay)</c>, or
/// <c>UiCalendar.Value(new UiDateRange(from, to))</c> for the parent to own.
/// </para>
/// <para>
/// The first click is held HERE, drawn as the start, and the model does not change; the second click writes the
/// whole range, in date order whichever end was clicked first. A bound model therefore never holds half a range.
/// Clicking again after that starts a new one.
/// </para>
/// </remarks>
[RaskChainEntry("UiCalendar")]
public sealed partial class UiCalendarRange : Component, IFormControl<UiDateRange>
{
    private DateOnly? _month;
    private DateOnly? _anchor;

    /// <summary>The accessible name — what the range is for.</summary>
    public required string Label { get; set; }

    /// <inheritdoc cref="UiCalendar.Month" />
    public DateOnly? Month { get; set; }

    /// <inheritdoc cref="UiCalendar.OnMonth" />
    public Callback<DateOnly>? OnMonth { get; set; }

    /// <inheritdoc cref="UiCalendar.Min" />
    public DateOnly? Min { get; set; }

    /// <inheritdoc cref="UiCalendar.Max" />
    public DateOnly? Max { get; set; }

    /// <inheritdoc cref="UiCalendar.FirstDay" />
    public DayOfWeek? FirstDay { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    /// <remarks>The chosen range. <c>default(UiDateRange)</c> is nothing chosen.</remarks>
    public UiDateRange Value { get; set; }

    /// <inheritdoc />
    public Callback<UiDateRange>? OnChange { get; set; }

    /// <inheritdoc />
    public Expression<Func<UiDateRange>>? Bind { get; set; }

    /// <inheritdoc />
    public Validator<UiDateRange>? Validate { get; set; }

    /// <inheritdoc />
    public Callback<UiDateRange>? AfterBind { get; set; }

    // The half-picked start and the view month are FIELDS, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var (acc, ctx, chosen) = UiFormCommit.Resolve<UiDateRange>(this);

        return UiDayGrid.Render(new UiDayGrid.View(
            Label,
            UiDayGrid.MonthOf(Month, _month, _anchor ?? (chosen == default ? null : chosen.Start)),
            FirstDay,
            Min,
            Max,
            Class,
            PageAsync,
            date => State(chosen, _anchor, date),
            date => PickAsync(date, acc, ctx),
            static _ => null));
    }

    /// <summary>How a day looks, given the committed range and a start still waiting for its end.</summary>
    internal static UiDayGrid.DayState State(UiDateRange chosen, DateOnly? anchor, DateOnly date) =>
        anchor is { } start
            ? new UiDayGrid.DayState(date == start, false)
            : new UiDayGrid.DayState(
                chosen != default && (date == chosen.Start || date == chosen.End),
                chosen.Contains(date));

    private Task PickAsync(DateOnly date, ExpressionAccessor.Accessor? acc, EditContext? ctx)
    {
        if (_anchor is not { } start)
        {
            _anchor = date;
            return Task.CompletedTask;
        }

        _anchor = null;
        return UiFormCommit.CommitAsync(this, acc, ctx, UiDateRange.Between(start, date));
    }

    private Task PageAsync(DateOnly month)
    {
        _month = month;
        return OnMonth?.Invoke(month) ?? Task.CompletedTask;
    }
}
