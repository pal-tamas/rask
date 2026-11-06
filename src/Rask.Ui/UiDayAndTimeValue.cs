using System.Linq.Expressions;

namespace Rask;

/// <summary>
///     What a <see cref="UiDayAndTime{T}" /> is bound to — one <c>DateTime</c> or <c>DateTime?</c> — taken apart into
///     its day and its time, and put together again from either.
/// </summary>
internal static class UiDayAndTimeValue
{
    /// <summary>Throws for a bound type that is no moment: the control binds a <c>DateTime</c> or a <c>DateTime?</c>.</summary>
    internal static void Check<T>()
    {
        if (typeof(T) != typeof(DateTime) && typeof(T) != typeof(DateTime?))
        {
            throw new InvalidOperationException(
                $"Ui.DayAndTime binds a DateTime or a DateTime?, not a {typeof(T).Name}. A day alone is Ui.DatePicker, a time alone Ui.TimePicker.");
        }
    }

    /// <summary>
    ///     Whether nothing chosen is an answer, as for a select's choices: a <c>DateTime?</c> the model does not require. A
    ///     <c>DateTime</c> always holds a moment, and a required member must be given one.
    /// </summary>
    /// <param name="bound">The control's bind expression, or null for a control bound to nothing.</param>
    internal static bool MayBeEmpty<T>(LambdaExpression? bound) => UiChoices.MayBeEmpty(bound, typeof(T));

    /// <summary>The moment <paramref name="value" /> holds; nothing for no value, or for a <c>DateTime</c> never set.</summary>
    internal static DateTime? Read<T>(T? value) => value is DateTime moment && moment != default ? moment : null;

    /// <summary>A moment as <typeparamref name="T" />: nothing becomes <see langword="null" />, or the unset <c>DateTime</c>.</summary>
    internal static T As<T>(DateTime? moment) => moment is { } chosen ? (T)(object)chosen : default!;

    /// <summary>The day of <paramref name="moment" />.</summary>
    internal static DateOnly? Day(DateTime? moment) => moment is { } whole ? DateOnly.FromDateTime(whole) : null;

    /// <summary>The time of day of <paramref name="moment" />, to the tick.</summary>
    internal static TimeOnly? Time(DateTime? moment) => moment is { } whole ? TimeOnly.FromDateTime(whole) : null;

    /// <summary>
    ///     The moment on another day, at the time it had — seconds and all; a first day is its first minute. No day is
    ///     no moment: a day taken away takes its time with it.
    /// </summary>
    internal static DateTime? OnDay(DateTime? moment, DateOnly? day) =>
        day is { } chosen ? Kept(moment, chosen.ToDateTime(Time(moment) ?? TimeOnly.MinValue)) : null;

    /// <summary>
    ///     The moment at another time of its day. A time showing the hour and the minute the moment already has
    ///     leaves it as it is: the field writes no seconds, and seconds the value held are not lost by a save. A time
    ///     taken away is the day's first minute; with no day there is no time to give.
    /// </summary>
    internal static DateTime? AtTime(DateTime? moment, TimeOnly? time)
    {
        if (moment is not { } whole)
        {
            return null;
        }

        if (time is { } then && then.Hour == whole.Hour && then.Minute == whole.Minute)
        {
            return whole;
        }

        return Kept(whole, DateOnly.FromDateTime(whole).ToDateTime(time ?? TimeOnly.MinValue));
    }

    // A moment the model held in UTC or local time stays so: only the day or the time changed.
    private static DateTime Kept(DateTime? was, DateTime now) => was is { } before ? DateTime.SpecifyKind(now, before.Kind) : now;
}
