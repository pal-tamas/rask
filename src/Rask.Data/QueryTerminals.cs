using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     The row-picking terminals <see cref="ModelQuery{TEntity}" /> and <see cref="Projection{TEntity, TResult}" />
///     share: <c>First</c> and <c>Single</c> throw where LINQ's do, with a message naming the call that does not.
/// </summary>
/// <remarks>
///     Each reads at most the rows it needs to decide — one for <c>First</c>, two for <c>Single</c>, as EF Core's
///     own operators do — so a projection to a value type can tell "no row" from a row holding the default.
/// </remarks>
internal static class QueryTerminals
{
    public static async Task<T> First<T>(IQueryable<T> query, string subject, CancellationToken cancellationToken)
    {
        var rows = await query.Take(1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : throw NoRow(subject, "First", "FirstOrDefault");
    }

    public static async Task<T> Single<T>(IQueryable<T> query, string subject, CancellationToken cancellationToken)
    {
        var rows = await query.Take(2).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count switch
        {
            1 => rows[0],
            0 => throw NoRow(subject, "Single", "SingleOrDefault"),
            _ => throw MoreThanOne(subject, "Single"),
        };
    }

    public static async Task<T?> SingleOrDefault<T>(IQueryable<T> query, string subject, CancellationToken cancellationToken)
    {
        var rows = await query.Take(2).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count switch
        {
            0 => default,
            1 => rows[0],
            _ => throw MoreThanOne(subject, "SingleOrDefault"),
        };
    }

    private static InvalidOperationException NoRow(string subject, string call, string orDefault) =>
        new($"No {subject} row matched .{call}(). Call .{orDefault}() instead when no row is an expected outcome — " +
            "it returns null rather than throwing.");

    private static InvalidOperationException MoreThanOne(string subject, string call) =>
        new($"More than one {subject} row matched .{call}(). Narrow the query, or call .First() when any one of them will do.");
}
