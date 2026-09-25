namespace Rask.Data;

/// <summary>
/// Full-text search: <see cref="FullTextQueryableExtensions.Search{TEntity}"/> finds the rows, and
/// <see cref="Highlight"/> and <see cref="Snippet"/> show the user why each one matched.
/// </summary>
/// <remarks>
/// <see cref="Highlight"/> and <see cref="Snippet"/> are translated to SQL — use them inside a <c>Select</c> on a
/// query that calls <c>Search</c>. They mark each matched term between <see cref="MatchStart"/> and
/// <see cref="MatchEnd"/> rather than with HTML, because the text is whatever was stored in the row: rendering
/// it as markup would hand anyone who can write a row a way to inject script. Render the result with
/// <c>UiHighlight</c>, which encodes the text and wraps each match in <c>&lt;mark&gt;</c>.
/// </remarks>
/// <example>
/// <code>
/// var hits = await Post.Search(query)
///     .Select(p =&gt; new { p.Id, Title = FullText.Highlight(p.Title), Excerpt = FullText.Snippet(p.Body) })
///     .ToListAsync();
/// </code>
/// </example>
public static class FullText
{
    /// <summary>Marks the start of a matched term in <see cref="Highlight"/> and <see cref="Snippet"/> output (U+E000, private use).</summary>
    public const char MatchStart = '';

    /// <summary>Marks the end of a matched term in <see cref="Highlight"/> and <see cref="Snippet"/> output (U+E001, private use).</summary>
    public const char MatchEnd = '';

    /// <summary>The whole value of <paramref name="property"/>, with every matched term marked.</summary>
    /// <param name="property">An indexed property of the searched entity, as in <c>p =&gt; FullText.Highlight(p.Title)</c>.</param>
    /// <returns>The marked text, or <see langword="null"/> when the property is <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">Always, when called outside a query — it only has meaning in SQL.</exception>
    public static string? Highlight(string? property) => throw OutsideQuery(nameof(Highlight));

    /// <summary>
    /// The part of <paramref name="property"/> around its best match, at most <paramref name="words"/> words long,
    /// with every matched term marked and <c>…</c> where text was cut.
    /// </summary>
    /// <param name="property">An indexed property of the searched entity, as in <c>p =&gt; FullText.Snippet(p.Body)</c>.</param>
    /// <param name="words">The most words to return, from 1 to 64. Defaults to 12.</param>
    /// <returns>The excerpt, or <see langword="null"/> when the property is <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">Always, when called outside a query — it only has meaning in SQL.</exception>
    public static string? Snippet(string? property, int words = 12) => throw OutsideQuery(nameof(Snippet));

    internal static InvalidOperationException OutsideQuery(string member) => new(
        $"FullText.{member} runs in the database: call it inside Select(...) on a query that calls Search(text), " +
        $"as in Post.Read.Search(text).Select(p => FullText.{member}(p.Title)).");
}
