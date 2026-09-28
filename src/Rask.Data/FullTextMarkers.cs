namespace Rask.Data;

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
    // match is FTS5 syntax (SQLite), tsQuery the same words as a tsquery (PostgreSQL).
    public static IOrderedQueryable<TEntity> Matching<TEntity>(IQueryable<TEntity> source, string match, string tsQuery) =>
        throw Unsupported<TEntity>();

    public static InvalidOperationException Unsupported<TEntity>() => new(
        $"Search(text) on {typeof(TEntity).Name} runs in the database, and this query's provider does not " +
        "support full-text search. Configure the context with UseRaskSqlite(services) from " +
        "Rask.SQLite.EntityFrameworkCore (or add .UseRaskFullTextSearch() to a plain UseSqlite) or with " +
        "UseRaskPostgres(services) from Rask.Postgres, and declare the index with HasFullTextSearch.");

    public static System.Reflection.MethodInfo MatchingMethod<TEntity>() =>
        new Func<IQueryable<TEntity>, string, string, IOrderedQueryable<TEntity>>(Matching).Method;

    public static bool IsMatching(System.Reflection.MethodInfo method) =>
        method.IsGenericMethod && method.DeclaringType == typeof(FullTextMarkers) && string.Equals(method.Name, nameof(Matching), StringComparison.Ordinal);
}
