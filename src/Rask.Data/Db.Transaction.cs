using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Rask.Wire;

namespace Rask.Data;

public static partial class Db
{
    /// <summary>
    ///     Runs <paramref name="work" /> in one database transaction: committed when it completes, rolled back when it
    ///     throws — and the exception rethrown.
    /// </summary>
    /// <param name="work">
    ///     The work. Every read (<c>Find</c>, <c>Where</c>, <c>Any</c>, <c>Count</c>, a read face) and every write
    ///     (<c>Create</c>, <c>Update</c>, <c>Save</c>, the unique checks) it makes WITHOUT a <c>db:</c> runs on this
    ///     transaction's connection, inside it.
    /// </param>
    /// <param name="isolation">
    ///     The isolation level to begin it at, or <see langword="null" /> to send none and get the database's own
    ///     default (read committed on SQL Server and PostgreSQL; SQLite is serializable, one writer at a time).
    /// </param>
    /// <param name="cancellationToken">Cancels the transaction; unset, the work this call belongs to does.</param>
    /// <returns>A task that completes once the transaction has committed.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Called inside another <c>Db.Transaction</c> with a different <paramref name="isolation" />.
    /// </exception>
    /// <remarks>
    ///     <code>
    /// await Db.Transaction(async () =>
    /// {
    ///     if (await Pistol.Count(p => ids.Contains(p.Id)) != ids.Count) return;
    ///     await Tank.Update(id, t => t.Change(...));
    /// }, isolation: IsolationLevel.Serializable);
    ///     </code>
    ///     <para>
    ///         A <c>Db.Transaction</c> inside another one joins it: same connection, same transaction, committed or
    ///         rolled back by the outermost. It is not a savepoint — a throw inside rolls back the whole work unless
    ///         the outer catches it — and it may not ask for another isolation level.
    ///     </para>
    ///     <para>
    ///         A context handed in as <c>db:</c> joins the work and stays the caller's: once the transaction ends,
    ///         however it ends, the context is out of it and on its own connection again. So does the app's own context
    ///         on the same database when it runs raw SQL inside the block (<c>ExecuteSqlRaw</c>, <c>SqlQueryRaw</c>): it
    ///         is enlisted before its first command and handed back the same way. Domain events, the outbox's wake-up and the data-changed notices go out after the commit, and not at
    ///         all after a rollback. The work shares one connection, so its reads and writes run one at a time
    ///         (no <c>Task.WhenAll</c> over them). On a context whose execution strategy retries, a transient
    ///         failure runs the whole work again from the top.
    ///     </para>
    /// </remarks>
    public static Task Transaction(Func<Task> work, IsolationLevel? isolation = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Transaction(
            async () =>
            {
                await work().ConfigureAwait(false);
                return true;
            },
            isolation,
            cancellationToken);
    }

    /// <summary>
    ///     Runs <paramref name="work" /> in one database transaction and hands back what it returned, once committed.
    /// </summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work; see <see cref="Transaction(Func{Task}, IsolationLevel?, CancellationToken)" />.</param>
    /// <param name="isolation">The isolation level, or <see langword="null" /> for the database's default.</param>
    /// <param name="cancellationToken">Cancels the transaction; unset, the work this call belongs to does.</param>
    /// <returns>What <paramref name="work" /> returned, after the commit.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Called inside another <c>Db.Transaction</c> with a different <paramref name="isolation" />.
    /// </exception>
    /// <remarks>
    ///     <code>
    /// var order = await Db.Transaction(async () => await Order.Create(model));
    ///     </code>
    /// </remarks>
    public static async Task<T> Transaction<T>(
        Func<Task<T>> work, IsolationLevel? isolation = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken = Ambient.Or(cancellationToken);

        if (AmbientTransaction.Current is { } outer)
        {
            RefuseAnotherIsolation(outer, isolation);
            return await work().ConfigureAwait(false);
        }

        var owner = CreateOwnContext();
        await using var ownerScope = owner.ConfigureAwait(false);

        // Inside the strategy, so a retrying one accepts a transaction it did not open — and suspends itself for
        // every context the work opens, which would otherwise refuse the transaction they join.
        var (result, afterCommit) = await owner.Database.CreateExecutionStrategy().ExecuteAsync(
            (owner, work, isolation),
            static (_, state, token) => Attempt(state.owner, state.work, state.isolation, token),
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);

        foreach (var after in afterCommit)
        {
            await after().ConfigureAwait(false);
        }

        return result;
    }

    private static async Task<(T Result, IReadOnlyList<Func<Task>> AfterCommit)> Attempt<T>(
        DbContext owner, Func<Task<T>> work, IsolationLevel? isolation, CancellationToken cancellationToken)
    {
        // No level asked, none sent: the database's own default, not one Rask picked for it.
        var transaction = isolation is { } level
            ? await owner.Database.BeginTransactionAsync(level, cancellationToken).ConfigureAwait(false)
            : await owner.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var transactionScope = transaction.ConfigureAwait(false);

        IReadOnlyList<Func<Task>> afterCommit;
        T result;
        using (var scope = AmbientTransaction.Enter(owner, transaction, isolation))
        {
            try
            {
                result = await work().ConfigureAwait(false);
            }
            catch
            {
                // The transaction is never committed now: what it could not take part in is taken back.
                foreach (var undo in scope.Abandon())
                {
                    await undo().ConfigureAwait(false);
                }

                throw;
            }

            afterCommit = scope.End();
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (result, afterCommit);
    }

    private static void RefuseAnotherIsolation(AmbientTransaction outer, IsolationLevel? asked)
    {
        if (asked is { } level && level != outer.Isolation)
        {
            throw new InvalidOperationException(
                $"This Db.Transaction asks for {level} isolation inside another Db.Transaction running at " +
                $"{outer.Isolation?.ToString() ?? "the database's default isolation"}. A nested Db.Transaction joins " +
                $"the outer one and cannot change its level: ask for {level} on the outermost Db.Transaction.");
        }
    }
}
