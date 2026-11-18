using System.Linq.Expressions;

namespace Rask;

/// <summary>
/// <c>Ui.DayAndTime.Bind(() =&gt; m.Day, () =&gt; m.Time)</c>: a day and a time the model keeps apart, each its own member.
/// </summary>
/// <remarks>
/// The day binds a <c>DateOnly?</c>, the time a <c>TimeOnly?</c>, and each says its own rules' messages under its own
/// part — two messages, when both fail. The one-member <c>Ui.DayAndTime.Bind(() =&gt; m.At)</c> binds a
/// <c>DateTime?</c> and says them under the pair.
/// </remarks>
public static class UiDayAndTimeSteps
{
    extension(RaskSeed_UiDayAndTime entry)
    {
        /// <summary>Binds the day to one member and the time of that day to another.</summary>
        /// <param name="day">The model's day: <c>() =&gt; m.Day</c>.</param>
        /// <param name="time">The model's time of that day: <c>() =&gt; m.Time</c>.</param>
        /// <returns>The pair, bound to both.</returns>
        public UiDayAndTime<DateTime?> Bind(Expression<Func<DateOnly?>> day, Expression<Func<TimeOnly?>> time) =>
            entry.Of<DateTime?>().BindParts(day, time);
    }
}
