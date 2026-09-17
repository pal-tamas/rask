using System.Linq.Expressions;
using System.Runtime.CompilerServices;

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

/// <summary>The <c>Search</c> operator on any EF Core query.</summary>
public static class FullTextQueryableExtensions
{
    private static readonly System.Reflection.FieldInfo BoxedValue =
        typeof(StrongBox<string>).GetField(nameof(StrongBox<string>.Value))!;

    /// <summary>
    /// The rows of <paramref name="source"/> whose indexed text contains every word of <paramref name="text"/>,
    /// best match first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entity must declare <c>HasFullTextSearch</c>. <paramref name="text"/> is what a user typed, taken
    /// literally: each word must appear (in any order, any case, ignoring diacritics), the last word also matches
    /// as a prefix, and characters that mean something to the database's query syntax are just text.
    /// </para>
    /// <para>
    /// The result is an ordinary query that keeps composing: <c>Where</c>, <c>Skip</c>/<c>Take</c> and
    /// <c>CountAsync</c> apply to the matches, and a later <c>OrderBy</c> replaces best-match order — which is what a
    /// grid's column sort should do. Text with no word in it (<see langword="null"/>, blank, only punctuation)
    /// filters nothing and returns <paramref name="source"/> unchanged, so an empty search box lists everything.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var page = await db.Set&lt;Post&gt;().Search(query).Where(p =&gt; p.Published).Take(20).ToListAsync();
    /// </code>
    /// </example>
    /// <typeparam name="TEntity">The searched entity.</typeparam>
    /// <param name="source">The query to search within.</param>
    /// <param name="text">The words to find.</param>
    /// <returns>The matching rows, best match first.</returns>
    public static IQueryable<TEntity> Search<TEntity>(this IQueryable<TEntity> source, string? text)
    {
        ArgumentNullException.ThrowIfNull(source);

        var match = FullTextQuery.Compile(text);
        if (match is null)
        {
            return source;
        }

        // LINQ to Objects would otherwise fail with "no method 'Matching' on type Enumerable", which names Rask's
        // plumbing rather than the mistake.
        if (source.Provider is EnumerableQuery)
        {
            throw FullTextMarkers.Unsupported<TEntity>();
        }

        // Boxed rather than a constant so EF Core sends it as a parameter and caches one plan for every search.
        var argument = Expression.Field(Expression.Constant(new StrongBox<string>(match)), BoxedValue);
        return source.Provider.CreateQuery<TEntity>(
            Expression.Call(FullTextMarkers.MatchingMethod<TEntity>(), source.Expression, argument));
    }
}

/// <summary>
/// The calls <c>Search</c> and <c>FullText</c> leave in a query's expression tree for the provider to rewrite.
/// Never executed.
/// </summary>
internal static class FullTextMarkers
{
    /// <summary>The marker for "filter <paramref name="source"/> to rows matching the compiled FTS query".</summary>
    /// <remarks>
    /// Ordered, because a search IS an ordering — best match first — so <c>ThenBy</c> can follow it, as
    /// <c>ModelQuery.Search</c> promises. The provider folds such a <c>ThenBy</c> into its rank order.
    /// </remarks>
    public static IOrderedQueryable<TEntity> Matching<TEntity>(IQueryable<TEntity> source, string match) =>
        throw Unsupported<TEntity>();

    public static InvalidOperationException Unsupported<TEntity>() => new(
        $"Search(text) on {typeof(TEntity).Name} runs in the database, and this query's provider does not " +
        "support full-text search. Configure the context with UseRaskSqlite(services) from " +
        "Rask.SQLite.EntityFrameworkCore, and declare the index with HasFullTextSearch.");

    public static System.Reflection.MethodInfo MatchingMethod<TEntity>() =>
        new Func<IQueryable<TEntity>, string, IOrderedQueryable<TEntity>>(Matching).Method;

    public static bool IsMatching(System.Reflection.MethodInfo method) =>
        method.IsGenericMethod && method.DeclaringType == typeof(FullTextMarkers) && method.Name == nameof(Matching);
}
