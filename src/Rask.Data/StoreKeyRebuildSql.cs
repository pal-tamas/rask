using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     The provider SQL of a store-key rebuild (<see cref="StoreKeyRebuildOperations" />): the guard that refuses pending
///     rows, the copy of the kept rows aside under their new ids, and their way back.
/// </summary>
/// <remarks>
///     <para>
///         A kept row's new id is a version-7 Guid made from what it already holds: the 48-bit Unix millisecond of its
///         <c>keepBy</c> instant, version 7, <c>rand_a</c> zero, the variant, and the old integer key as the last fifteen hex
///         digits — <c>xxxxxxxx-xxxx-7000-8iii-iiiiiiiiiiii</c>. The same row always gets the same id, two rows never share
///         one, and the ids sort by that instant, then by the old key, as the ids made afterwards sort after them.
///     </para>
///     <para>
///         The rows are set aside in an ordinary table beside the one rebuilt, not a temporary one: an idempotent script runs
///         each SQL Server operation in an <c>EXEC</c> of its own, which drops a <c>#temp</c> table as it returns.
///     </para>
/// </remarks>
/// <param name="provider">The provider's name.</param>
/// <param name="sql">The provider's SQL helper.</param>
/// <param name="table">The table rebuilt.</param>
/// <param name="schema">Its schema, or null for the default one.</param>
/// <param name="lowerText">
///     Whether the key is a typed id Rask writes as lower-case text on SQLite; a plain Guid is EF Core's upper-case text there.
/// </param>
internal sealed class StoreKeyRebuildSql(string provider, ISqlGenerationHelper sql, string table, string? schema, bool lowerText)
{
    private const string Millisecond = "rask_ms";
    private const string OldKey = "rask_key";

    // SQLite's printf: upper-case hex digits for EF Core's Guid text, lower for a typed id's.
    private string Hex => lowerText ? "x" : "X";

    private bool IsSqlServer => string.Equals(provider, ProviderNames.SqlServer, StringComparison.Ordinal);

    private bool IsPostgres => string.Equals(provider, ProviderNames.Postgres, StringComparison.Ordinal);

    /// <summary>The table rebuilt, delimited.</summary>
    internal string Table => sql.DelimitIdentifier(table, schema);

    /// <summary>The table the kept rows wait in while the table is rebuilt, delimited.</summary>
    internal string Aside => sql.DelimitIdentifier(table + "_rask_rebuild", schema);

    /// <summary><paramref name="name" />, delimited.</summary>
    internal string Delimit(string name) => sql.DelimitIdentifier(name);

    /// <summary>The marker that opens the rebuild: an exclusive migration, which no older version of the app may run beside.</summary>
    internal string Marker => ExclusiveMigrations.Prefix + ": rebuilds the key of " + Table + " as a client-made Guid";

    /// <summary>SQL that fails the migration while a row of the table has no <paramref name="pending" /> column yet.</summary>
    internal string Guard(string pending)
    {
        var waiting = $"SELECT 1 FROM {Table} WHERE {sql.DelimitIdentifier(pending)} IS NULL";
        var message = $"Rask cannot rebuild the key of {table}: rows with no {pending} are still waiting. "
                      + "Let them finish (or delete them), then migrate again.";
        if (IsSqlServer)
        {
            return $"IF EXISTS ({waiting})\n    THROW 50000, N'{Quoted(message)}', 1;";
        }

        if (IsPostgres)
        {
            return $"DO $rask$ BEGIN\n    IF EXISTS ({waiting}) THEN\n        RAISE EXCEPTION '{Quoted(message)}';\n    END IF;\nEND $rask$;";
        }

        // SQLite has no IF and no RAISE outside a trigger: a CHECK on a scratch table, named for the message, is refused
        // as "CHECK constraint failed: <message>".
        var guard = sql.DelimitIdentifier(table + "_rask_guard");
        return $"CREATE TEMP TABLE {guard} (waiting INTEGER NOT NULL CONSTRAINT {sql.DelimitIdentifier(message)} CHECK (waiting = 0));\n"
               + $"INSERT INTO {guard} (waiting) SELECT COUNT(*) FROM {Table} WHERE {sql.DelimitIdentifier(pending)} IS NULL;\n"
               + $"DROP TABLE {guard};";
    }

    /// <summary>
    ///     SQL that copies every row aside: the new id as <paramref name="key" />, then <paramref name="columns" />, each read
    ///     through <paramref name="read" /> (SQL Server's stored Guid form, where the target stores them by time).
    /// </summary>
    internal string SetAside(string key, string keepBy, bool storedForm, IReadOnlyList<string> columns, Func<string, string> read)
    {
        var id = NewId(storedForm);
        var select = new StringBuilder("SELECT ").Append(id).Append(" AS ").Append(sql.DelimitIdentifier(key));
        foreach (var column in columns)
        {
            select.Append(", ").Append(read(column)).Append(" AS ").Append(sql.DelimitIdentifier(column));
        }

        var rows = $"(SELECT *, {MillisecondOf(sql.DelimitIdentifier(keepBy))} AS {Millisecond}, "
                   + $"{HexOf(sql.DelimitIdentifier(key))} AS {OldKey} FROM {Table}) AS rask_rows";
        return IsSqlServer
            ? $"{select} INTO {Aside} FROM {rows};"
            : $"CREATE TABLE {Aside} AS {select} FROM {rows};";
    }

    /// <summary>SQL that writes the rows set aside into the rebuilt table, and drops the table they waited in.</summary>
    internal string Restore(string key, IReadOnlyList<string> columns)
    {
        var names = string.Join(", ", new[] { key }.Concat(columns).Select(c => sql.DelimitIdentifier(c)));
        return $"INSERT INTO {Table} ({names})\nSELECT {names} FROM {Aside};\nDROP TABLE {Aside};";
    }

    // The new id, from the hex digits of the row's millisecond and its old key, as text, then as the column's type.
    private string NewId(bool storedForm)
    {
        var text = Concatenated(
            Part(Millisecond, 1, 8), "'-'", Part(Millisecond, 9, 4), "'-7000-8'", Part(OldKey, 1, 3), "'-'", Part(OldKey, 4, 12));
        if (IsSqlServer)
        {
            return storedForm ? SqlServerGuidOrder.StoredFromText(text) : $"CONVERT(uniqueidentifier, {text})";
        }

        return IsPostgres ? $"CAST({text} AS uuid)" : text;
    }

    // Twelve hex digits of the instant's Unix millisecond.
    private string MillisecondOf(string column)
    {
        if (IsSqlServer)
        {
            return $"RIGHT(CONVERT(varchar(16), CONVERT(binary(8), DATEDIFF_BIG(millisecond, '1970-01-01T00:00:00', {column})), 2), 12)";
        }

        return IsPostgres
            ? $"lpad(to_hex(CAST(floor(extract(epoch FROM {column}) * 1000) AS bigint)), 12, '0')"
            : $"printf('%012{Hex}', CAST(round(unixepoch({column}, 'subsec') * 1000) AS INTEGER))";
    }

    // Fifteen hex digits of the old integer key.
    private string HexOf(string column)
    {
        if (IsSqlServer)
        {
            return $"RIGHT(CONVERT(varchar(16), CONVERT(binary(8), CAST({column} AS bigint)), 2), 15)";
        }

        return IsPostgres ? $"lpad(to_hex(CAST({column} AS bigint)), 15, '0')" : $"printf('%015{Hex}', {column})";
    }

    private string Part(string column, int start, int length) =>
        IsSqlServer || IsPostgres
            ? $"SUBSTRING(rask_rows.{column}, {start}, {length})"
            : $"substr(rask_rows.{column}, {start}, {length})";

    private string Concatenated(params string[] parts) => string.Join(IsSqlServer ? " + " : " || ", parts);

    private static string Quoted(string text) => text.Replace("'", "''", StringComparison.Ordinal);
}
