using System.Text.Json;

namespace Rask.Data;

/// <summary>
///     A column being named by <c>ColumnNames</c>: the property it holds, and the names Rask can already give it —
///     <c>c.ColumnNames(n =&gt; n.CamelCase)</c>.
/// </summary>
public sealed class ColumnName
{
    internal ColumnName(string table, IReadOnlyList<string> path, string asDeclared)
    {
        Table = table;
        Path = path;
        AsDeclared = asDeclared;
    }

    /// <summary>The table (or view) the column is in, as the database names it.</summary>
    public string Table { get; }

    /// <summary>
    ///     The property's name, after the value objects and owned types that hold it: <c>["CountedAt"]</c>, or
    ///     <c>["Km", "Start"]</c> for <c>Start</c> inside a <c>Km</c> value object.
    /// </summary>
    public IReadOnlyList<string> Path { get; }

    /// <summary>What EF Core names it: <c>CountedAt</c>, <c>Km_Start</c>.</summary>
    public string AsDeclared { get; }

    /// <summary>The path in camel case, as one word: <c>countedAt</c>, <c>kmStart</c>, <c>vatNumber</c> for <c>VATNumber</c>.</summary>
    public string CamelCase => JsonNamingPolicy.CamelCase.ConvertName(string.Concat(Path));

    /// <summary>Each step of the path in snake case, joined by <c>_</c>: <c>counted_at</c>, <c>km_start</c>.</summary>
    public string SnakeCase => string.Join('_', Path.Select(JsonNamingPolicy.SnakeCaseLower.ConvertName));
}
