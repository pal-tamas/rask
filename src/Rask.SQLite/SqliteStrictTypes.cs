namespace Rask.SQLite;

/// <summary>
/// The column types a <c>STRICT</c> table accepts, and the check that reports a rejected one before
/// SQLite does.
/// </summary>
/// <remarks>
/// A <c>STRICT</c> table (SQLite 3.37+) requires every column to declare a type, and that type must be
/// one of exactly six names. SQLite's own error for a violation names only the offending type, not the
/// table or column it came from, which is a poor thing to meet on startup — hence
/// <see cref="Describe"/>.
/// </remarks>
public static class SqliteStrictTypes
{
    /// <summary>
    /// The six type names <c>STRICT</c> allows. <c>ANY</c> opts a single column back out of enforcement
    /// and, unlike an untyped column in an ordinary table, applies no affinity conversion at all.
    /// </summary>
    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "INT", "INTEGER", "REAL", "TEXT", "BLOB", "ANY" };

    /// <summary>
    /// Whether <paramref name="columnType"/> is usable in a <c>STRICT</c> table. A type carrying a size
    /// or precision (<c>VARCHAR(50)</c>, <c>NUMERIC(9,2)</c>) is not: <c>STRICT</c> matches the six names
    /// exactly rather than by affinity.
    /// </summary>
    public static bool IsAllowed(string? columnType) =>
        columnType is not null && Allowed.Contains(columnType.Trim());

    /// <summary>
    /// The message for a column a <c>STRICT</c> table cannot hold, naming where it came from and how to
    /// resolve it.
    /// </summary>
    /// <param name="table">The table being created.</param>
    /// <param name="column">The offending column.</param>
    /// <param name="columnType">Its declared type.</param>
    public static string Describe(string table, string column, string? columnType) =>
        $"STRICT tables are enabled, but '{table}'.'{column}' declares the column type " +
        $"'{columnType ?? "(none)"}', which SQLite does not accept in a STRICT table. Allowed types are " +
        $"{string.Join(", ", Allowed.Order(StringComparer.Ordinal))}. Entity Framework Core's default " +
        "SQLite types are all allowed, so this comes from an explicit HasColumnType(...) — change it to " +
        "one of the allowed names, use ANY to exempt this column, or turn STRICT off in UseRaskSqlite.";
}
