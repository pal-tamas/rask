using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Rask.Values;

namespace Rask.Data;

/// <summary>
///     Reads a moment's day on the app's wall calendar in a query — <c>f.FueledAt.Day</c>, which
///     <c>GroupBy(f =&gt; f.FueledAt.Day)</c> and <c>Where(f =&gt; f.FueledAt.Day == day)</c> read — from a column that keeps
///     the moment as UTC, through the provider's <see cref="IMomentDays" />: the same day on every database.
/// </summary>
/// <remarks>
///     <para>
///         The zone is <see cref="Clock.Zone" /> as the query is translated: SQL Server and PostgreSQL write it into the
///         SQL, SQLite reads it as each row is. An app sets its zone once, as it starts.
///     </para>
///     <para>
///         A part of the time read off <c>.Value</c> of such a column — <c>f.FueledAt.Value.Date</c>, <c>.Year</c>,
///         <c>.Hour</c> — would be the UTC instant's, an hour or two off the wall clock's near midnight, so it is refused
///         with the way to say it: <c>.Day</c> for the day, a comparison of moments for a range of time.
///     </para>
/// </remarks>
/// <param name="typeMappings">The provider's type mappings.</param>
/// <param name="days">The provider's way to write a moment's day; none where the provider has no Rask package.</param>
internal sealed class MomentDayTranslator(IRelationalTypeMappingSource typeMappings, IEnumerable<IMomentDays> days)
    : IMemberTranslatorPlugin, IMemberTranslator
{
    private static readonly HashSet<string> TimeParts = new(StringComparer.Ordinal)
    {
        nameof(DateTime.Date), nameof(DateTime.Year), nameof(DateTime.Month), nameof(DateTime.Day), nameof(DateTime.Hour),
        nameof(DateTime.Minute), nameof(DateTime.Second), nameof(DateTime.Millisecond), nameof(DateTime.DayOfWeek),
        nameof(DateTime.DayOfYear), nameof(DateTime.TimeOfDay),
    };

    private readonly IMomentDays? _days = days.LastOrDefault();

    public IEnumerable<IMemberTranslator> Translators => [this];

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is not ColumnExpression { TypeMapping: { } stored } column)
        {
            return null;
        }

        if (member.DeclaringType == typeof(DateTime) && TimeParts.Contains(member.Name) && IsUtcMoment(stored))
        {
            throw new InvalidOperationException(
                $"'{column.Name}.Value.{member.Name}' would read the UTC instant the column keeps, not the app's wall clock. "
                + "Read the day as '.Day' (a DateOnly on the wall calendar), and a stretch of time by comparing moments: "
                + "'f.At >= from && f.At < to'.");
        }

        if (_days is null || member.DeclaringType != typeof(Moment) ||
            !string.Equals(member.Name, nameof(Moment.Day), StringComparison.Ordinal) || !IsUtcMoment(stored))
        {
            return null;
        }

        var mapping = typeMappings.FindMapping(typeof(DateTime), stored.StoreType) ?? typeMappings.FindMapping(typeof(DateTime));
        return mapping is null
            ? null
            : _days.WallDay(
                new ColumnExpression(column.Name, column.TableAlias, column.Column, typeof(DateTime), mapping, column.IsNullable), Clock.Zone);
    }

    // A column Rask keeps a moment in as UTC: the moment's own column, or its `.Value` read through WallClockInUtc.
    private static bool IsUtcMoment(RelationalTypeMapping mapping) =>
        ReferenceEquals(mapping.Converter, WallClockInUtc.Converter) ||
        ReferenceEquals(OneValueConverters.StoredAs(mapping.Converter), WallClockInUtc.Converter);
}
