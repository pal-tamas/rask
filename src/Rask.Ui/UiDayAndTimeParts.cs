using System.Linq.Expressions;

namespace Rask;

/// <summary>What <c>Ui.DayAndTime.Bind(() =&gt; m.Day, () =&gt; m.Time)</c> bound: the day's member and the time's.</summary>
/// <param name="Day">The model's day.</param>
/// <param name="Time">The model's time of that day.</param>
internal sealed record UiDayAndTimeParts(Expression<Func<DateOnly?>> Day, Expression<Func<TimeOnly?>> Time);
