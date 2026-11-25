using System.Data.Common;
using System.Linq.Expressions;
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

    // What the tag line becomes once rewritten, so a context that carries two interceptors which rewrite locks it once.
    private const string Rewritten = Marker + ", locked";

    /// <summary>Rewrites <paramref name="command" /> when it is a read tagged <see cref="Tag" />.</summary>
    internal static void Apply(DbCommand command, DbContext? context)
    {
        if (context is not null && TagAt(command.CommandText) is var at and >= 0)
        {
            command.CommandText = Locked(context.Database.ProviderName, command.CommandText, at);
        }
    }

    /// <summary>
    ///     Refuses a count, an <c>Any</c> or an <c>All</c> of a query tagged <see cref="Tag" /> — what
    ///     <see cref="ForUpdateQueryableExtensions.ForUpdate{T}" /> cannot refuse itself, since the terminal is the standard
    ///     one: refused as the query is compiled, on every database.
    /// </summary>
    /// <param name="query">The whole query, its terminal outermost.</param>
    internal static void RefuseAggregate(Expression query)
    {
        if (query is MethodCallExpression { Method: { DeclaringType: var declaring, Name: var terminal } } call
            && declaring == typeof(Queryable)
            && terminal is nameof(Queryable.Count) or nameof(Queryable.LongCount) or nameof(Queryable.Any) or nameof(Queryable.All)
            && IsTagged(call.Arguments[0]))
        {
            throw Aggregate(terminal);
        }
    }

    // Where the tag line sits among the comment lines EF Core heads a tagged query with, or -1 when the query is not tagged
    // or was rewritten already — so a context that carries two interceptors which rewrite locks it once.
    private static int TagAt(string sql)
    {
        var at = 0;
        while (sql.AsSpan(at).StartsWith("--", StringComparison.Ordinal))
        {
            var end = sql.IndexOf('\n', at);
            var line = sql.AsSpan(at, (end < 0 ? sql.Length : end) - at).TrimEnd('\r');
            if (line.SequenceEqual(Marker))
            {
                return at;
            }

            if (end < 0)
            {
                return -1;
            }

            at = end + 1;
            while (at < sql.Length && sql[at] is '\r' or '\n')
            {
                at++;
            }
        }

        return -1;
    }

    // The rewrite, its tag line marked as done. The tag heads the statement, so the rewrite leaves it where it was.
    private static string Locked(string? provider, string sql, int at) =>
        Rewrite(provider, sql).Remove(at, Marker.Length).Insert(at, Rewritten);

    // Whether the query a terminal is applied to was read ForUpdate(), anywhere along it.
    private static bool IsTagged(Expression source)
    {
        while (source is MethodCallExpression call)
        {
            if (string.Equals(call.Method.Name, nameof(EntityFrameworkQueryableExtensions.TagWith), StringComparison.Ordinal)
                && call.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                && call.Arguments[1] is ConstantExpression { Value: Tag })
            {
                return true;
            }

            if (call.Arguments.Count == 0)
            {
                return false;
            }

            source = call.Arguments[0];
        }

        return false;
    }

    /// <summary><paramref name="sql" /> with its first table read under an update lock, as <paramref name="provider" /> says it.</summary>
    internal static string Rewrite(string? provider, string sql) => provider switch
    {
        ProviderNames.SqlServer => SqlServerTable().Replace(sql, "$0 WITH (UPDLOCK, HOLDLOCK)", 1),
        ProviderNames.Postgres when PostgresTable().Match(sql) is { Success: true } table =>
            sql.TrimEnd() + "\nFOR UPDATE OF " + table.Groups["alias"].Value,
        _ => sql,
    };

    /// <summary>The refusal of a count, or an <c>Any</c>, of a query read <c>ForUpdate()</c>.</summary>
    /// <param name="subject">The read face, as the app writes it: <c>Product</c>.</param>
    /// <param name="terminal">The terminal called: <c>Count</c>.</param>
    internal static InvalidOperationException Aggregate(string subject, string terminal) =>
        new($"{subject}.Where(…).ForUpdate().{terminal}() locks nothing: a lock is held on the rows a read hands back, "
            + "and PostgreSQL holds none for an aggregate. Read the rows and count them: "
            + $"var rows = await {subject}.Where(…).ForUpdate();");

    /// <summary>The refusal of a count, an <c>Any</c> or an <c>All</c> of a query of a set read <c>ForUpdate()</c>.</summary>
    /// <param name="terminal">The terminal called: <c>Count</c>.</param>
    internal static InvalidOperationException Aggregate(string terminal) =>
        new($"query.ForUpdate().{terminal}() locks nothing: a lock is held on the rows a read hands back, "
            + "and PostgreSQL holds none for an aggregate. Read the rows and count them: "
            + "var rows = await query.ForUpdate().ToListAsync();");

    // FROM [dbo].[Periods] AS [p]
    [GeneratedRegex(@"FROM\s+(?:\[[^\]]+\]\.)?\[[^\]]+\]\s+AS\s+\[[^\]]+\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SqlServerTable();

    // FROM app."Periods" AS p
    [GeneratedRegex(@"FROM\s+(?:(?:""[^""]+""|\w+)\.)?(?:""[^""]+""|\w+)\s+AS\s+(?<alias>""[^""]+""|\w+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PostgresTable();
}
