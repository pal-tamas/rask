using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     The transaction <see cref="Db.Transaction(Func{Task}, IsolationLevel?, CancellationToken)" /> holds open, and
///     what every context opened inside it joins.
/// </summary>
/// <remarks>
///     <para>
///         A context is still opened per read and per write, and disposed by whoever opened it — joining only points
///         it at this connection and this transaction. Its owner never disposes them, so a context closing does not
///         end the work around it.
///     </para>
///     <para>
///         What must wait for the commit — a data change told to a session, a domain event published, the outbox
///         woken — is queued here instead of on the joined context, which commits nothing itself and is long
///         disposed by the time this commits.
///     </para>
/// </remarks>
internal sealed class AmbientTransaction
{
    private static readonly AsyncLocal<AmbientTransaction?> Open = new();

    private readonly DbConnection _connection;
    private readonly string? _connectionString;
    private readonly DbTransaction _transaction;
    private readonly List<Func<Task>> _afterCommit = [];
    private readonly List<Func<Task>> _onRollback = [];
    private readonly List<AdmittedContext> _admitted = [];
    private bool _ended;

    private AmbientTransaction(DbContext owner, DbTransaction transaction, IsolationLevel? isolation)
    {
        _connection = owner.Database.GetDbConnection();
        _connectionString = owner.Database.GetConnectionString();
        _transaction = transaction;
        Isolation = isolation;
    }

    /// <summary>The level the outermost call asked for, or <see langword="null" /> for the database's default.</summary>
    internal IsolationLevel? Isolation { get; }

    /// <summary>
    ///     The transaction in progress in this flow, or <see langword="null" />. One that has ended — a task started
    ///     inside and still running after the commit — is not in progress: that task opens contexts of its own again.
    /// </summary>
    internal static AmbientTransaction? Current => Open.Value is { _ended: false } open ? open : null;

    /// <summary>The ambient transaction <paramref name="context" /> joined, or <see langword="null" />.</summary>
    internal static AmbientTransaction? Of(DbContext? context) =>
        context is not null && Current is { } current &&
        ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), current._transaction)
            ? current
            : null;

    /// <summary>Makes <paramref name="transaction" /> the one every context in this flow joins, until disposed.</summary>
    internal static Scope Enter(DbContext owner, IDbContextTransaction transaction, IsolationLevel? isolation)
    {
        var entered = new AmbientTransaction(owner, transaction.GetDbTransaction(), isolation);
        return new Scope(entered);
    }

    /// <summary>
    ///     <paramref name="context" />, freshly opened by its caller, enlisted in the transaction in progress — or as it
    ///     was when there is none. A battery's queue (<c>Jobs.Enqueue</c>, <c>Mail.Send</c>) opens its context here, so
    ///     its row commits with the work and is gone on a rollback.
    /// </summary>
    /// <exception cref="InvalidOperationException">The context is on another database than the transaction.</exception>
    /// <remarks>
    ///     Not handed back: a joined context is the opener's own, opened for this one write and disposed by it.
    /// </remarks>
    internal static TContext Join<TContext>(TContext context)
        where TContext : DbContext
    {
        if (Current is not { } current)
        {
            return context;
        }

        try
        {
            current.Enlist(context);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     A context the caller handed in as <c>db:</c>, enlisted in the transaction in progress when there is one. It is
    ///     the caller's: never disposed here, and handed back as it came — out of the transaction, on its own connection —
    ///     however the transaction ends.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     The context is on another database, is in a transaction of its own, or has its connection open already.
    /// </exception>
    internal static void Admit(DbContext context)
    {
        if (Current is { } current && current.Enlist(context) is { } admitted)
        {
            current._admitted.Add(admitted);
        }
    }

    // Points the context at this transaction's connection and enlists it — refusing, never silently running it outside.
    // Hands back what it was on before, or null when it had joined already.
    private AdmittedContext? Enlist(DbContext context)
    {
        if (ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), _transaction))
        {
            return null;
        }

        if (context.Database.CurrentTransaction is not null)
        {
            throw Refused(context, "is in a transaction of its own");
        }

        if (!string.Equals(context.Database.GetConnectionString(), _connectionString, StringComparison.Ordinal))
        {
            throw Refused(context, "is on another database (its connection string differs)");
        }

        var connection = context.Database.GetDbConnection();
        if (!ReferenceEquals(connection, _connection) && connection.State != ConnectionState.Closed)
        {
            throw Refused(context, "already has its own connection open");
        }

        var admitted = AdmittedContext.Capture(context);
        context.Database.SetDbConnection(_connection, contextOwnsConnection: false);
        context.Database.UseTransaction(_transaction);
        return admitted;
    }

    // Every db: context back as its caller handed it in. One its caller disposed meanwhile has nothing to get back.
    private void HandBack()
    {
        foreach (var admitted in _admitted)
        {
            try
            {
                admitted.Restore(_transaction);
            }
            catch (ObjectDisposedException)
            {
                // Disposed by its caller inside the work: nobody holds it any more.
            }
        }

        _admitted.Clear();
    }

    private static InvalidOperationException Refused(DbContext context, string why) =>
        new($"A {context.GetType().Name} used inside Db.Transaction {why}, so it cannot join the transaction — and " +
            "running it outside would let it commit what the transaction rolls back. Use a context on the same " +
            "connection string, opened inside the Db.Transaction, or move this work out of it.");

    /// <summary>Runs <paramref name="work" /> once this transaction commits; never, when it rolls back.</summary>
    internal void AfterCommit(Func<Task> work) => _afterCommit.Add(work);

    /// <summary>Runs <paramref name="work" /> once this transaction commits; never, when it rolls back.</summary>
    internal void AfterCommit(Action work) =>
        _afterCommit.Add(() =>
        {
            work();
            return Task.CompletedTask;
        });

    /// <summary>
    ///     Runs <paramref name="undo" /> once the work throws and this transaction rolls back; never, when it commits.
    ///     For what was written where the transaction cannot reach — a file's bytes on a disk or in a bucket — so a
    ///     rollback takes it back too. It runs outside the transaction, and must not throw: it is a cleanup on the way
    ///     out of a failure, and the failure is what reaches the caller.
    /// </summary>
    internal void OnRollback(Func<Task> undo) => _onRollback.Add(undo);

    /// <summary>What the outermost call holds: the flow's binding, restored on dispose.</summary>
    internal sealed class Scope : IDisposable
    {
        private readonly AmbientTransaction? _previous;
        private readonly AmbientTransaction _entered;

        internal Scope(AmbientTransaction entered)
        {
            _previous = Open.Value;
            _entered = entered;
            Open.Value = entered;
        }

        /// <summary>Ends the transaction for this flow and hands back what waits for its commit.</summary>
        internal IReadOnlyList<Func<Task>> End()
        {
            _entered._ended = true;
            Open.Value = _previous;
            return _entered._afterCommit;
        }

        /// <summary>Ends the transaction for this flow, the work having thrown, and hands back what undoes it.</summary>
        internal IReadOnlyList<Func<Task>> Abandon()
        {
            _entered._ended = true;
            Open.Value = _previous;
            return _entered._onRollback;
        }

        /// <summary>Ends the transaction for this flow and hands every <c>db:</c> context back as it came.</summary>
        public void Dispose()
        {
            _entered._ended = true;
            Open.Value = _previous;
            _entered.HandBack();
        }
    }
}
