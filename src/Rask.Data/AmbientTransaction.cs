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
        // An event published inside reaches the live pages that handle it once this commits, and not on a rollback.
        LiveEvents.DeferUntilCommit ??= DeferUntilCommit;

        var entered = new AmbientTransaction(owner, transaction.GetDbTransaction(), isolation);
        return new Scope(entered);
    }

    private static bool DeferUntilCommit(Action work)
    {
        if (Current is not { } open)
        {
            return false;
        }

        open.AfterCommit(work);
        return true;
    }

    /// <summary>
    ///     <paramref name="context" />, freshly opened by its caller, enlisted in the transaction in progress — or as it
    ///     was when there is none. A battery's queue (<c>Jobs.Enqueue</c>, <c>Mail.Send</c>) opens its context here, so
    ///     its row commits with the work and is gone on a rollback.
    /// </summary>
    /// <exception cref="InvalidOperationException">The context is on another database than the transaction.</exception>
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
    ///     the caller's: never disposed here, whatever happens.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     The context is on another database, is in a transaction of its own, or has its connection open already.
    /// </exception>
    internal static void Admit(DbContext context) => Current?.Enlist(context);

    // Points the context at this transaction's connection and enlists it — refusing, never silently running it outside.
    private void Enlist(DbContext context)
    {
        if (ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), _transaction))
        {
            return;
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

        context.Database.SetDbConnection(_connection, contextOwnsConnection: false);
        context.Database.UseTransaction(_transaction);
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

        public void Dispose()
        {
            _entered._ended = true;
            Open.Value = _previous;
        }
    }
}
