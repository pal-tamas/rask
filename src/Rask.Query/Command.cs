using System.Diagnostics.CodeAnalysis;
using Rask.Cqrs;

namespace Rask.Query;

/// <summary>
///     The shared state machine behind every command shape — a record command and a function alike:
///     pending/error/success, the components watching it, and any optimistic edits to roll back.
/// </summary>
internal sealed class CommandCore(QueryClient client)
{
    private readonly List<IOptimisticUpdate> _optimistic = [];
    private readonly ComponentReaders _readers = new();

    public CommandStatus Status { get; private set; } = CommandStatus.Idle;

    public Exception? Error { get; private set; }

    public void Observe() => _readers.Observe();

    public void AddOptimistic(IOptimisticUpdate update) => _optimistic.Add(update);

    public void Reset()
    {
        Status = CommandStatus.Idle;
        Error = null;
        _readers.RenderAll();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Whatever the handler threw belongs on the command as Error, for the component "
                        + "to render. See the remarks on Command<TCommand>.SendAsync for why it is not rethrown.")]
    public async Task<TResult?> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> dispatch,
        Action invalidate,
        CancellationToken cancellationToken)
    {
        Status = CommandStatus.Pending;
        Error = null;
        _readers.RenderAll();

        // Snapshots first, and all of them, before anything is dispatched: a rollback that only
        // covers the edits made before the failure leaves the rest applied.
        var snapshots = new IOptimisticSnapshot[_optimistic.Count];
        for (var i = 0; i < _optimistic.Count; i++)
        {
            snapshots[i] = _optimistic[i].Apply(client);
        }

        try
        {
            var result = await dispatch(cancellationToken).ConfigureAwait(false);
            Status = CommandStatus.Success;

            // The invalidation replaces the optimistic guess with what the server actually holds, so
            // there is nothing to undo on success.
            invalidate();
            _readers.RenderAll();
            return result;
        }
        catch (Exception ex)
        {
            // Undone in reverse, so overlapping edits to one entry unwind in the order they were made.
            for (var i = snapshots.Length - 1; i >= 0; i--)
            {
                snapshots[i].Restore(client);
            }

            Error = ex;
            Status = CommandStatus.Error;
            _readers.RenderAll();
            return default;
        }
    }
}

/// <summary>
///     A command you can render: whether it is running, whether it failed, and what to disable while
///     it is in flight.
/// </summary>
/// <remarks>
///     <para>
///         Hold one in a field and read it from <c>Render</c>:
///     </para>
///     <code>
///     private readonly Command&lt;ShipOrder&gt; _ship = q.Command&lt;ShipOrder&gt;();
///
///     Button.Disabled(_ship.IsPending)
///           .OnClick(() =&gt; _ship.SendAsync(new ShipOrder(id)))
///           [_ship.IsPending ? "Shipping…" : "Ship"]
///     </code>
///     <para>
///         <see cref="SendAsync" /> does <b>not</b> throw. It is called from an event handler, where an
///         exception has nowhere to go and would surface as an unhandled framework error rather than
///         as something the screen can show. The failure lands on <see cref="Error" /> and
///         <see cref="Status" />, which is where a component can actually render it. Use
///         <c>IQueryClient.SendAsync</c> when you want the exception.
///     </para>
/// </remarks>
/// <typeparam name="TCommand">The command this dispatches.</typeparam>
public sealed class Command<TCommand>
    where TCommand : ICommand
{
    private readonly QueryClient _client;
    private readonly CommandCore _core;

    internal Command(QueryClient client)
    {
        _client = client;
        _core = new CommandCore(client);
    }

    /// <summary>Where this command is in its lifecycle.</summary>
    public CommandStatus Status
    {
        get
        {
            _core.Observe();
            return _core.Status;
        }
    }

    /// <summary>Whatever the last run threw, or null.</summary>
    public Exception? Error
    {
        get
        {
            _core.Observe();
            return _core.Error;
        }
    }

    /// <summary>Running now. This is what disables the button.</summary>
    public bool IsPending => Status == CommandStatus.Pending;

    /// <summary>The last run succeeded.</summary>
    public bool IsSuccess => Status == CommandStatus.Success;

    /// <summary>The last run failed; see <see cref="Error" />.</summary>
    public bool IsError => Status == CommandStatus.Error;

    /// <summary>
    ///     Edits a cached query's result before the server answers, and puts it back if the command
    ///     fails.
    /// </summary>
    /// <remarks>
    ///     Register these once, when the command is created. On success the command's
    ///     <see cref="InvalidatesAttribute" /> refetches and replaces the guess with the truth; on
    ///     failure the previous value is restored, because a screen still showing the optimistic
    ///     result after a refused save is worse than never having shown it.
    /// </remarks>
    /// <typeparam name="TResult">The query's result type.</typeparam>
    /// <param name="query">The query whose cached result to edit.</param>
    /// <param name="update">Produces the optimistic result from the current one.</param>
    /// <returns>This command, for chaining.</returns>
    public Command<TCommand> Optimistic<TResult>(IQuery<TResult> query, Func<TResult, TResult> update)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(update);
        _core.AddOptimistic(new OptimisticUpdate<TResult>(query, update));
        return this;
    }

    /// <summary>Dispatches the command. Never throws — see the remarks on the type.</summary>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    public Task SendAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _core.RunAsync<object?>(
            async ct =>
            {
                await _client.DispatchCommandAsync(command, ct).ConfigureAwait(false);
                return null;
            },
            () => _client.InvalidateDeclared(command),
            cancellationToken);
    }

    /// <summary>Returns to <see cref="CommandStatus.Idle" />, clearing any error.</summary>
    public void Reset() => _core.Reset();
}

/// <summary>
///     A command that returns a value, rendered the same way as <see cref="Command{TCommand}" />.
/// </summary>
/// <typeparam name="TCommand">The command this dispatches.</typeparam>
/// <typeparam name="TResult">What the command returns.</typeparam>
public sealed class Command<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly QueryClient _client;
    private readonly CommandCore _core;

    internal Command(QueryClient client)
    {
        _client = client;
        _core = new CommandCore(client);
    }

    /// <summary>Where this command is in its lifecycle.</summary>
    public CommandStatus Status
    {
        get
        {
            _core.Observe();
            return _core.Status;
        }
    }

    /// <summary>What the last successful run returned, or <c>default</c>.</summary>
    public TResult? Data { get; private set; }

    /// <summary>Whatever the last run threw, or null.</summary>
    public Exception? Error
    {
        get
        {
            _core.Observe();
            return _core.Error;
        }
    }

    /// <summary>Running now. This is what disables the button.</summary>
    public bool IsPending => Status == CommandStatus.Pending;

    /// <summary>The last run succeeded.</summary>
    public bool IsSuccess => Status == CommandStatus.Success;

    /// <summary>The last run failed; see <see cref="Error" />.</summary>
    public bool IsError => Status == CommandStatus.Error;

    /// <inheritdoc cref="Command{TCommand}.Optimistic{TQueryResult}" />
    /// <typeparam name="TQueryResult">The query's result type.</typeparam>
    /// <param name="query">The query whose cached result to edit.</param>
    /// <param name="update">Produces the optimistic result from the current one.</param>
    /// <returns>This command, for chaining.</returns>
    public Command<TCommand, TResult> Optimistic<TQueryResult>(
        IQuery<TQueryResult> query,
        Func<TQueryResult, TQueryResult> update)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(update);
        _core.AddOptimistic(new OptimisticUpdate<TQueryResult>(query, update));
        return this;
    }

    /// <summary>Dispatches the command. Never throws — the failure lands on <see cref="Error" />.</summary>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    /// <returns>What the command returned, or <c>default</c> if it failed.</returns>
    public async Task<TResult?> SendAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Data = await _core
            .RunAsync(
                ct => _client.DispatchCommandAsync(command, ct),
                () => _client.InvalidateDeclared(command),
                cancellationToken)
            .ConfigureAwait(false);
        return Data;
    }

    /// <summary>Returns to <see cref="CommandStatus.Idle" />, clearing any error and result.</summary>
    public void Reset()
    {
        Data = default;
        _core.Reset();
    }
}

/// <summary>
///     A command that is a function rather than an <see cref="ICommand" /> record — a third-party
///     HTTP call, a file write — rendered the same way as <see cref="Command{TCommand}" />.
/// </summary>
/// <remarks>
///     <para>
///         Created once with what it makes out of date, then handed the work on every send, so the
///         lambda captures what this click is about:
///     </para>
///     <code>
///     private readonly Command _ship = q.Command(invalidates: "orders");
///
///     Button.Disabled(_ship.IsPending)
///           .OnClick(() =&gt; _ship.SendAsync(ct =&gt; api.ShipAsync(id, ct)))
///           ["Ship"]
///     </code>
///     <para>
///         A record declares its invalidation with <see cref="InvalidatesAttribute" />; a function has
///         nowhere to put one, so it is named here instead, where the command is created. Prefer a
///         record wherever there is one: there the invalidation travels with the command to every
///         screen that sends it.
///     </para>
/// </remarks>
public sealed class Command
{
    private readonly QueryClient _client;
    private readonly QueryKey[] _invalidates;
    private readonly CommandCore _core;

    internal Command(QueryClient client, QueryKey[] invalidates)
    {
        _client = client;
        _invalidates = invalidates;
        _core = new CommandCore(client);
    }

    /// <summary>Where this command is in its lifecycle.</summary>
    public CommandStatus Status
    {
        get
        {
            _core.Observe();
            return _core.Status;
        }
    }

    /// <summary>Whatever the last send threw, or null.</summary>
    public Exception? Error
    {
        get
        {
            _core.Observe();
            return _core.Error;
        }
    }

    /// <summary>Running now. This is what disables the button.</summary>
    public bool IsPending => Status == CommandStatus.Pending;

    /// <summary>The last send succeeded.</summary>
    public bool IsSuccess => Status == CommandStatus.Success;

    /// <summary>The last send failed; see <see cref="Error" />.</summary>
    public bool IsError => Status == CommandStatus.Error;

    /// <summary>
    ///     Runs <paramref name="send" />, then invalidates the keys this command was created with.
    ///     Never throws — the failure lands on <see cref="Error" />.
    /// </summary>
    /// <param name="send">The work, given the cancellation token.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public Task SendAsync(Func<CancellationToken, Task> send, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(send);
        return _core.RunAsync<object?>(
            async ct =>
            {
                await send(ct).ConfigureAwait(false);
                return null;
            },
            Invalidate,
            cancellationToken);
    }

    /// <summary>
    ///     Runs <paramref name="send" /> and returns what it produced, then invalidates the keys this
    ///     command was created with. Never throws — the failure lands on <see cref="Error" />.
    /// </summary>
    /// <typeparam name="TResult">What the work returns, inferred from the lambda.</typeparam>
    /// <param name="send">The work, given the cancellation token.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What the work returned, or <c>default</c> if it failed.</returns>
    public Task<TResult?> SendAsync<TResult>(
        Func<CancellationToken, Task<TResult>> send,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(send);
        return _core.RunAsync(send, Invalidate, cancellationToken);
    }

    /// <summary>Returns to <see cref="CommandStatus.Idle" />, clearing any error.</summary>
    public void Reset() => _core.Reset();

    private void Invalidate()
    {
        foreach (var key in _invalidates)
        {
            _client.Invalidate(key);
        }
    }
}
