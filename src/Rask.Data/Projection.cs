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

    /// <summary>Runs the query and hands back every projected row: <c>await Product.All.Select(p =&gt; p.Name)</c>.</summary>
    public System.Runtime.CompilerServices.TaskAwaiter<List<TResult>> GetAwaiter() =>
        RunAsync(static (q, ct) => q.ToListAsync(ct), default).GetAwaiter();

    /// <summary>Runs the query as <see cref="GetAwaiter" /> does, resuming on the captured context or not.</summary>
    /// <param name="continueOnCapturedContext">Whether to resume on the context the await started on.</param>
    public System.Runtime.CompilerServices.ConfiguredTaskAwaitable<List<TResult>> ConfigureAwait(bool continueOnCapturedContext) =>
        RunAsync(static (q, ct) => q.ToListAsync(ct), default).ConfigureAwait(continueOnCapturedContext);

    /// <summary>The first projected row, or the default when the query matched nothing.</summary>
    public Task<TResult?> First(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.FirstOrDefaultAsync(ct), cancellationToken)!;

    /// <summary>How many rows match.</summary>
    public Task<int> Count(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.CountAsync(ct), cancellationToken);

    /// <summary>Whether the query matches any row.</summary>
    public Task<bool> Any(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.AnyAsync(ct), cancellationToken);

    private async Task<TValue> RunAsync<TValue>(
        Func<IQueryable<TResult>, CancellationToken, Task<TValue>> run,
        CancellationToken cancellationToken)
    {
        var context = ReadDb.OpenFor<TEntity>();
        await using var contextScope = context.ConfigureAwait(false);
        var projected = _source(context.Set<TEntity>()).Select(_selector);
        return await run(projected, Ambient.Or(cancellationToken)).ConfigureAwait(false);
    }
}
