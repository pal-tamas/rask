using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Rask.SQLite;

/// <summary>
///     Registers, on one open connection, the comparison <c>x.Loosely()</c> asks for: the
///     <see cref="SqliteCollations.SearchOrder" /> collation, and <see cref="Function" />, which matches one text inside
///     another by it.
/// </summary>
/// <remarks>
///     A collation reaches <c>=</c> and <c>ORDER BY</c> but no function, so <c>Contains</c>, <c>StartsWith</c> and
///     <c>EndsWith</c> on a loosely compared text are written as <c>rask_search(text, value, how)</c> instead of
///     <c>instr</c> and <c>LIKE</c>. Like every registration, both are dropped when a pooled connection is returned, so this
///     runs on every open.
/// </remarks>
internal static class SqliteSearchText
{
    /// <summary>The function's name in SQL.</summary>
    internal const string Function = "rask_search";

    /// <summary>What the comparison ignores: case, accents, kana and width.</summary>
    internal const CompareOptions Ignored =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    /// <summary><see cref="Function" />'s last argument: where in the text the value must be.</summary>
    internal enum Place
    {
        Anywhere = 0,
        Start = 1,
        End = 2,
    }

    private static CompareInfo Compare => CultureInfo.InvariantCulture.CompareInfo;

    /// <summary>Registers the comparison on <paramref name="connection" />.</summary>
    /// <param name="connection">An open connection.</param>
    internal static void Apply(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // A collation runs inside SQLite's comparison loop, where a managed exception cannot unwind: total by construction.
        connection.CreateCollation(SqliteCollations.SearchOrder, static (x, y) => SqliteTextOrder.Compare(Compare, Ignored, x, y));
        connection.CreateFunction<string?, string?, long, bool?>(
            Function, static (text, value, place) => Holds(text, value, (Place)place), isDeterministic: true);
    }

    /// <summary>Whether <paramref name="text" /> holds <paramref name="value" /> at <paramref name="place" />; null for a null.</summary>
    internal static bool? Holds(string? text, string? value, Place place)
    {
        if (text is null || value is null)
        {
            return null;
        }

        return place switch
        {
            Place.Start => Compare.IsPrefix(text, value, Ignored),
            Place.End => Compare.IsSuffix(text, value, Ignored),
            _ => Compare.IndexOf(text, value, Ignored) >= 0,
        };
    }
}
