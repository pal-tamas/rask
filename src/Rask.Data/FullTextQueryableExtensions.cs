using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Rask.Data;

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

        // Boxed rather than constants so EF Core sends them as parameters and caches one plan for every search. Both
        // syntaxes travel, because the words are compiled here and the provider is only known once the query is:
        // SQLite's rewrite reads the FTS5 one and PostgreSQL's the tsquery, and EF sends only the one referenced.
        var argument = Expression.Field(Expression.Constant(new StrongBox<string>(match)), BoxedValue);
        var tsQuery = Expression.Field(
            Expression.Constant(new StrongBox<string>(FullTextQuery.CompileTsQuery(text)!)), BoxedValue);
        return source.Provider.CreateQuery<TEntity>(
            Expression.Call(FullTextMarkers.MatchingMethod<TEntity>(), source.Expression, argument, tsQuery));
    }
}
