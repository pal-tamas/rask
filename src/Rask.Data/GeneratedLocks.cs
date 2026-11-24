using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     The row locks a generated entry takes — <c>b.Operation(x =&gt; x.M, forUpdate: true)</c>,
///     <c>forUpdate: s =&gt; s.Parts</c>, <c>await id.Load(version, forUpdate: true)</c> — in the database's own words:
///     <c>UPDLOCK, HOLDLOCK</c> on SQL Server, <c>FOR UPDATE</c> on PostgreSQL; SQLite's one writer serializes already.
/// </summary>
/// <remarks>
///     Called by generated code. Each lock is held to the end of the <c>Db.Transaction</c> it is taken in, so another
///     writer of the same rows waits for its commit instead of reading what it is about to change.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedLocks
{
    /// <summary>Locks the row of the <typeparamref name="TAggregate" /> with <paramref name="key" />.</summary>
    /// <typeparam name="TAggregate">The aggregate.</typeparam>
    /// <param name="key">Its id.</param>
    /// <param name="cancellationToken">Cancels the read; unset, the work this call belongs to does.</param>
    /// <returns>A task that completes once the row is locked.</returns>
    /// <exception cref="InvalidOperationException">Called outside a <c>Db.Transaction</c>.</exception>
    public static async Task Row<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        object key, CancellationToken cancellationToken = default)
        where TAggregate : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(key);
        DemandTransaction(typeof(TAggregate).Name);
        cancellationToken = Ambient.Or(cancellationToken);

        var context = Db.CreateContext();
        await using var contextScope = context.ConfigureAwait(false);
        var primary = context.Model.FindEntityType(typeof(TAggregate))?.FindPrimaryKey()?.Properties.Single()
                      ?? throw new InvalidOperationException($"{typeof(TAggregate).Name} is not in the model of {context.GetType().Name}.");
        await ParentRead<TAggregate>.ForUpdate(context, primary, [key]).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Locks the rows of one child collection of the <typeparamref name="TAggregate" /> with <paramref name="key" /> —
    ///     the ones there are, and, on SQL Server, the room for one more.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate.</typeparam>
    /// <typeparam name="TChild">The child the collection holds.</typeparam>
    /// <param name="key">The aggregate's id.</param>
    /// <param name="navigation">The collection's name: <c>"Parts"</c>.</param>
    /// <param name="cancellationToken">Cancels the read; unset, the work this call belongs to does.</param>
    /// <returns>A task that completes once the rows are locked.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Called outside a <c>Db.Transaction</c>, or <paramref name="navigation" /> is no collection of the aggregate's
    ///     children keyed by one column.
    /// </exception>
    public static async Task Children<
        [DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate,
        [DynamicallyAccessedMembers(DataTrimming.Entity)] TChild>(
        object key, string navigation, CancellationToken cancellationToken = default)
        where TAggregate : class, IAggregate
        where TChild : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(navigation);
        DemandTransaction(typeof(TAggregate).Name + "." + navigation);
        cancellationToken = Ambient.Or(cancellationToken);

        var context = Db.CreateContext();
        await using var contextScope = context.ConfigureAwait(false);
        var parent = context.Model.FindEntityType(typeof(TAggregate))?.FindNavigation(navigation)?.ForeignKey;
        if (parent is not { Properties: [var column] } || parent.DeclaringEntityType.ClrType != typeof(TChild))
        {
            throw new InvalidOperationException(
                $"forUpdate: names {typeof(TAggregate).Name}.{navigation}, which is no collection of its children keyed " +
                $"by one column. Name a child collection: b.Operation(x => x.M, forUpdate: x => x.{navigation}).");
        }

        await ParentRead<TChild>.ForUpdate(context, column, [key]).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    // A lock taken outside a transaction ends with its own statement: it would hold nothing.
    internal static void DemandTransaction(string what)
    {
        if (AmbientTransaction.Current is null)
        {
            throw new InvalidOperationException(
                $"Locking {what} for update outside Db.Transaction holds nothing: the lock ends with the read. Take it " +
                "inside the transaction that writes: await Db.Transaction(async () => { … });");
        }
    }
}
