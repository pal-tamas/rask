using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Rask.Data;

/// <summary>
///     How a provider writes the day a UTC moment falls on, on the app's wall calendar: what a query's
///     <c>f.Moment.Day</c> reads — <c>GroupBy(f =&gt; f.FueledAt.Day)</c> — over a column of moments kept as UTC.
///     Registered by each Rask provider; <see cref="MomentDayTranslator" /> asks it.
/// </summary>
internal interface IMomentDays
{
    /// <summary>The day <paramref name="utc" /> (a <see cref="DateTime" /> column holding UTC) falls on in <paramref name="zone" />, as a <see cref="DateOnly" />.</summary>
    /// <param name="utc">The column, typed as the <see cref="DateTime" /> it stores, with no converter.</param>
    /// <param name="zone">The app's zone, <see cref="Clock.Zone" />, as the query is translated.</param>
    /// <returns>The expression of the day.</returns>
    SqlExpression WallDay(SqlExpression utc, TimeZoneInfo zone);
}
