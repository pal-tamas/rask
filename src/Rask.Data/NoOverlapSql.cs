using System.Globalization;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The statements that hold a <see cref="NoOverlapRule" /> on PostgreSQL and SQL Server, and take it away again.
/// </summary>
/// <remarks>
///     <para>
///         <b>PostgreSQL</b>: an exclusion constraint over <c>btree_gist</c> — <c>key WITH =</c> for each key column and
///         the half-open range <c>WITH &amp;&amp;</c>, filtered by the rule's rows. A NULL in the key is never equal, a NULL
///         end is unbounded and an empty range overlaps nothing — PostgreSQL's own semantics, which the other two follow.
///         The extension is created first (<c>IF NOT EXISTS</c>: it is a trusted one, so the database's owner may).
///     </para>
///     <para>
///         <b>SQL Server</b>: an index over the key and the start, and an <c>AFTER INSERT, UPDATE</c> trigger that looks for
///         another row the new one overlaps under <c>UPDLOCK, HOLDLOCK</c> — the key range stays locked to the end of the
///         transaction, so a second save at the same moment waits for the first and then sees its row (or is chosen as a
///         deadlock victim): never both. It refuses with <c>THROW</c> <see cref="SqlServerError" />, the rule's name as
///         the message.
///     </para>
/// </remarks>
internal static class NoOverlapSql
{
    /// <summary>The error number SQL Server's trigger throws.</summary>
    internal const int SqlServerError = 51101;

    /// <summary>The statements that create <paramref name="rule" /> on <paramref name="table" />.</summary>
    internal static IEnumerable<string> Create(string provider, NoOverlapRule rule, ITable table, IReadOnlyList<string> primaryKey, int limit) =>
        provider switch
        {
            ProviderNames.Postgres => Postgres(rule, table),
            ProviderNames.SqlServer => SqlServer(rule, table, primaryKey, limit),
            _ => [],
        };

    /// <summary>The statements that drop <paramref name="rule" /> from the table it was created on.</summary>
    internal static IEnumerable<string> Drop(string provider, NoOverlapRule rule, string table, string? schema, int limit) =>
        provider switch
        {
            ProviderNames.Postgres =>
                [$"ALTER TABLE {Pg(table, schema)} DROP CONSTRAINT IF EXISTS {Pg(rule.Name)};"],
            ProviderNames.SqlServer =>
            [
                $"DROP TRIGGER IF EXISTS {Ss(Trigger(rule.Name, limit), schema)};",
                $"DROP INDEX IF EXISTS {Ss(Index(rule.Name, limit))} ON {Ss(table, schema)};",
            ],
            _ => [],
        };

    private static IEnumerable<string> Postgres(NoOverlapRule rule, ITable table)
    {
        var parts = rule.Keys.Select(static k => $"{Pg(k)} WITH =")
            .Append($"{RangeOf(table, rule.From)}({Pg(rule.From)}, {Pg(rule.To)}, '[)') WITH &&");
        var where = rule.Rows.Length > 0 ? $" WHERE ({rule.Rows})" : "";

        yield return "CREATE EXTENSION IF NOT EXISTS btree_gist;";
        yield return $"ALTER TABLE {Pg(table.Name, table.Schema)} ADD CONSTRAINT {Pg(rule.Name)} EXCLUDE USING gist ({string.Join(", ", parts)}){where};";
    }

    // The range type of the start's column: what PostgreSQL makes the half-open range of.
    private static string RangeOf(ITable table, string column)
    {
        var type = table.FindColumn(column)?.StoreType.ToLowerInvariant() ?? "";
        return type switch
        {
            _ when type.StartsWith("timestamp with time zone", StringComparison.Ordinal) || string.Equals(type, "timestamptz", StringComparison.Ordinal) => "tstzrange",
            _ when type.StartsWith("timestamp", StringComparison.Ordinal) => "tsrange",
            "date" => "daterange",
            "integer" or "int" or "int4" or "smallint" or "int2" => "int4range",
            "bigint" or "int8" => "int8range",
            _ when type.StartsWith("numeric", StringComparison.Ordinal) || type.StartsWith("decimal", StringComparison.Ordinal) => "numrange",
            _ => throw new InvalidOperationException(
                $"NoOverlap over '{table.Name}.{column}', a {type} column: PostgreSQL makes a range of a moment, a day, a whole "
                + "number or a decimal, and of nothing else."),
        };
    }

    private static IEnumerable<string> SqlServer(NoOverlapRule rule, ITable table, IReadOnlyList<string> primaryKey, int limit)
    {
        var live = rule.Rows.Length > 0 ? rule.Rows : "1 = 1";
        var same = rule.Keys.Count == 0 ? "1 = 1" : string.Join(" AND ", rule.Keys.Select(static k => $"x.{Ss(k)} = n.{Ss(k)}"));
        var self = string.Join(" AND ", primaryKey.Select(static k => $"x.{Ss(k)} = n.{Ss(k)}"));
        var (from, to) = (Ss(rule.From), Ss(rule.To));
        var name = rule.Name.Replace("'", "''", StringComparison.Ordinal);

        yield return $"CREATE INDEX {Ss(Index(rule.Name, limit))} ON {Ss(table.Name, table.Schema)} ({string.Join(", ", rule.Keys.Append(rule.From).Select(Ss))});";
        yield return string.Create(CultureInfo.InvariantCulture, $"""
            CREATE TRIGGER {Ss(Trigger(rule.Name, limit), table.Schema)} ON {Ss(table.Name, table.Schema)} AFTER INSERT, UPDATE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1
                    FROM (SELECT * FROM inserted WHERE {live}) AS n
                    JOIN (SELECT * FROM {Ss(table.Name, table.Schema)} WITH (UPDLOCK, HOLDLOCK) WHERE {live}) AS x ON {same}
                    WHERE NOT ({self})
                      AND (n.{to} IS NULL OR n.{from} < n.{to}) AND (x.{to} IS NULL OR x.{from} < x.{to})
                      AND (n.{to} IS NULL OR x.{from} < n.{to}) AND (x.{to} IS NULL OR n.{from} < x.{to}))
                    THROW {SqlServerError}, N'{name}', 1;
            END;
            """);
    }

    /// <summary>The name a rule's trigger takes on SQL Server: <c>tr_</c> in place of <c>xc_</c>.</summary>
    internal static string Trigger(string rule, int limit) => Limited("tr_" + rule[3..], limit);

    private static string Index(string rule, int limit) => Limited("ix_" + rule[3..], limit);

    private static string Limited(string name, int limit) => name.Length > limit ? name[..limit] : name;

    private static string Pg(string name) => $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Pg(string name, string? schema) => schema is null ? Pg(name) : $"{Pg(schema)}.{Pg(name)}";

    private static string Ss(string name) => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string Ss(string name, string? schema) => schema is null ? Ss(name) : $"{Ss(schema)}.{Ss(name)}";
}
