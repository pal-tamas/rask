using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
///     What SQLite, which knows no time zone, is given to keep moments as UTC: the rewrite of a column kept as the app's
///     wall clock (<see cref="MomentsInUtc" />), and the functions it calls — <see cref="ToUtc" /> and
///     <see cref="WallDay" /> — registered on every connection a <c>UseRaskSqlite</c> context opens.
/// </summary>
/// <remarks>
///     A moment is stored as EF Core writes a <see cref="DateTime" />: <c>yyyy-MM-dd HH:mm:ss.FFFFFFF</c>, now in UTC,
///     which orders as the time does. The functions read <see cref="Clock.Zone" /> as they run, so a test's zone is
///     followed, and convert by the rule a <c>Moment</c> follows (<see cref="WallClockInUtc" />).
/// </remarks>
internal static class SqliteMoments
{
    /// <summary>The function that reads a wall-clock moment of the app's zone as the UTC instant it is.</summary>
    internal const string ToUtc = "rask_moment_utc";

    /// <summary>The function that reads a UTC moment as its day on the app's wall calendar, <c>yyyy-MM-dd</c>.</summary>
    internal const string WallDay = "rask_moment_wall_day";

    private const string Format = "yyyy-MM-dd HH:mm:ss.FFFFFFF";

    /// <summary>Registers the functions on <paramref name="connection" />.</summary>
    internal static void Apply(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?>(ToUtc, static wall => Convert(wall, WallClockInUtc.ToUtc, Format), isDeterministic: false);
        connection.CreateFunction<string?, string?>(WallDay, static utc => Convert(utc, WallClockInUtc.ToWall, "yyyy-MM-dd"), isDeterministic: false);
    }

    /// <summary><paramref name="operations" />, each rewrite of a column of moments written as the <c>UPDATE</c> that calls <see cref="ToUtc" />.</summary>
    internal static IReadOnlyList<MigrationOperation> Written(IReadOnlyList<MigrationOperation> operations, ISqlGenerationHelper sql) =>
        MomentsInUtc.Written(operations, (table, schema, column) =>
        {
            var quoted = sql.DelimitIdentifier(column);
            return $"UPDATE {sql.DelimitIdentifier(table, schema)} SET {quoted} = {ToUtc}({quoted}) WHERE {quoted} IS NOT NULL{sql.StatementTerminator}";
        });

    // Text a function cannot read is handed back as it is: a collation or function runs inside SQLite, where a managed
    // exception cannot unwind, and the rewrite must not stop over a row it cannot read.
    private static string? Convert(string? stored, Func<DateTime, DateTime> convert, string format) =>
        stored is not null && DateTime.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.None, out var read)
            ? convert(DateTime.SpecifyKind(read, DateTimeKind.Unspecified)).ToString(format, CultureInfo.InvariantCulture)
            : stored;
}
