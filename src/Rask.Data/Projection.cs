using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     A query that projects <typeparamref name="TEntity" /> rows to <typeparamref name="TResult" />,
///     produced by <see cref="ModelQuery{TEntity}.Select{TResult}" />.
/// </summary>
/// <remarks>
///     Read-only by construction: a projection is not an entity, so there is nothing to save or delete
///     through it.
/// </remarks>
/// <typeparam name="TEntity">The entity being read.</typeparam>
/// <typeparam name="TResult">What each row is projected to.</typeparam>
public sealed class Projection<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TResult>
    where TEntity : class
{
    private readonly Func<IQueryable<TEntity>, IQueryable<TEntity>> _source;
    private readonly Expression<Func<TEntity, TResult>> _selector;

    internal Projection(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> source,
        Expression<Func<TEntity, TResult>> selector)
    {
        _source = source;
        _selector = selector;
    }

    /// <summary>Runs the query and returns every projected row.</summary>
#pragma warning disable MA0016 // a fresh list the caller owns, as EF Core's ToListAsync returns
    public Task<List<TResult>> ToListAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.ToListAsync(ct), cancellationToken);
#pragma warning restore MA0016

    /// <summary>Runs the query and returns every projected row as an array.</summary>
    public Task<TResult[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.ToArrayAsync(ct), cancellationToken);

    /// <summary>Runs the query and returns the first projected row, or the default when empty.</summary>
    public Task<TResult?> FirstOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.FirstOrDefaultAsync(ct), cancellationToken)!;

    /// <summary>Counts the matching rows.</summary>
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.CountAsync(ct), cancellationToken);

    /// <summary>Whether the query matches any row.</summary>
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.AnyAsync(ct), cancellationToken);

    private async Task<TValue> RunAsync<TValue>(
        Func<IQueryable<TResult>, CancellationToken, Task<TValue>> run,
        CancellationToken cancellationToken)
    {
        var context = ReadDb.OpenFor<TEntity>();
        await using var contextScope = context.ConfigureAwait(false);
        var projected = _source(context.Set<TEntity>()).Select(_selector);
        return await run(projected, cancellationToken).ConfigureAwait(false);
    }
}
