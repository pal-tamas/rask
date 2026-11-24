namespace Rask.Data;

/// <summary>Text compared case and accents aside, inside a query — the way a search box looks.</summary>
public static class SearchTextExtensions
{
    /// <summary>
    ///     The text, compared case and accents aside: <c>Driver.Where(d =&gt; d.Name.Loosely().Contains(typed))</c> finds
    ///     <c>Kovács</c> for <c>"kovacs"</c> and <c>Árvíztűrő</c> for <c>"ARVIZ"</c>.
    /// </summary>
    /// <param name="text">A text column.</param>
    /// <returns>Never returns: it is only written into a query's SQL.</returns>
    /// <remarks>
    ///     <para>
    ///         <c>Contains</c>, <c>StartsWith</c>, <c>EndsWith</c>, <c>==</c> and <c>OrderBy</c> on it ignore case, accents,
    ///         kana and width — the comparison is no one language's, so <c>"c"</c> is found in <c>Csaba</c> even where the
    ///         app's own order (<c>OrderTextBy("Hungarian_100_CI_AS")</c>) counts <c>cs</c> as one letter. Only the query
    ///         it is written in changes; the column, its collation and every other comparison on it stay as they are.
    ///     </para>
    ///     <para>
    ///         On SQL Server it is <c>[Name] COLLATE Latin1_General_100_CI_AI</c>. On SQLite through <c>UseRaskSqlite</c>
    ///         (or a browser's <c>UseRaskFullTextSearch</c>) it is <c>COLLATE "RASK_SEARCH"</c>, registered on every
    ///         connection, and <c>Contains</c>, <c>StartsWith</c> and <c>EndsWith</c> are matched by the same comparison. On
    ///         PostgreSQL through <c>UseRaskPostgres</c> it is <c>COLLATE rask_search</c>, a nondeterministic ICU collation
    ///         the migrations create (PostgreSQL 18 or later matches <c>LIKE</c> by it). Any other provider refuses the
    ///         query with the reason.
    ///     </para>
    ///     <para>
    ///         It runs in the database: a list already in memory compares with
    ///         <c>CompareInfo.IndexOf(text, typed, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace)</c>.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Called outside a query the database runs.</exception>
    public static string Loosely(this string text) =>
        throw new InvalidOperationException(
            "x.Loosely() runs in the database, in a query: Driver.Where(d => d.Name.Loosely().Contains(typed)). A list "
            + "already in memory compares with CultureInfo.InvariantCulture.CompareInfo.IndexOf(name, typed, "
            + "CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0.");
}
