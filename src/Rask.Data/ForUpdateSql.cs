using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     Reads the rows of a query tagged <see cref="Tag" /> under an update lock, in the database's own words: the read
///     <c>LockedBy(…, forUpdate: true)</c> makes of a save's parents.
/// </summary>
/// <remarks>
///     EF Core writes no lock hint, so the tagged command is rewritten as it is executed: the query's first table — the
///     parents' — takes <c>WITH (UPDLOCK, HOLDLOCK)</c> on SQL Server, and the query ends <c>FOR UPDATE OF</c> its alias
///     on PostgreSQL. SQLite is left as written: its one writer serializes the save already. A query of another provider
///     is left as written too, and locks nothing.
/// </remarks>
internal static partial class ForUpdateSql
{
    /// <summary>The tag the read is marked with.</summary>
    internal const string Tag = "rask:for-update";

    private const string Marker = "-- " + Tag;

    /// <summary>Rewrites <paramref name="command" /> when it is a read tagged <see cref="Tag" />.</summary>
    internal static void Apply(DbCommand command, DbContext? context)
    {
        if (context is not null && command.CommandText.StartsWith(Marker, StringComparison.Ordinal))
        {
            command.CommandText = Rewrite(context.Database.ProviderName, command.CommandText);
        }
    }

    /// <summary><paramref name="sql" /> with its first table read under an update lock, as <paramref name="provider" /> says it.</summary>
    internal static string Rewrite(string? provider, string sql) => provider switch
    {
        ProviderNames.SqlServer => SqlServerTable().Replace(sql, "$0 WITH (UPDLOCK, HOLDLOCK)", 1),
        ProviderNames.Postgres when PostgresTable().Match(sql) is { Success: true } table =>
            sql.TrimEnd() + "\nFOR UPDATE OF " + table.Groups["alias"].Value,
        _ => sql,
    };

    // FROM [dbo].[Periods] AS [p]
    [GeneratedRegex(@"FROM\s+(?:\[[^\]]+\]\.)?\[[^\]]+\]\s+AS\s+\[[^\]]+\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SqlServerTable();

    // FROM app."Periods" AS p
    [GeneratedRegex(@"FROM\s+(?:(?:""[^""]+""|\w+)\.)?(?:""[^""]+""|\w+)\s+AS\s+(?<alias>""[^""]+""|\w+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PostgresTable();
}
