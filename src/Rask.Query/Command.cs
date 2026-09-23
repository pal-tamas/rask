using System.Diagnostics.CodeAnalysis;
using Rask.Cqrs;

namespace Rask.Query;

/// <summary>
///     The shared state machine behind every command shape — a record command and a function alike:
///     pending/error/success, the components watching it, and the optimistic edits of a send to roll back.
/// </summary>
internal sealed class CommandCore
{
    private readonly ComponentReaders _readers = new();

    public CommandStatus Status { get; private set; } = CommandStatus.Idle;

    public Exception? Error { get; private set; }

    public void Observe() => _readers.Observe();

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
                        + "to render. See the remarks on Command<TCommand>.Send for why it is not rethrown.")]
    public async Task<TResult?> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> dispatch,
        Action invalidate,
        OptimisticEdit[] optimistic,
        CancellationToken cancellationToken)
    {
        Status = CommandStatus.Pending;
        Error = null;
        _readers.RenderAll();

        // Snapshots first, and all of them, before anything is dispatched: a rollback that only
        // covers the edits made before the failure leaves the rest applied.
        var snapshots = optimistic.Length == 0 ? [] : new IOptimisticSnapshot[optimistic.Length];
        for (var i = 0; i < optimistic.Length; i++)
        {
            snapshots[i] = optimistic[i].Apply();
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
                snapshots[i].Restore();
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
///           .OnClick(async () =&gt; await _ship.Send(new ShipOrder(id)))
///           [_ship.IsPending ? "Shipping…" : "Ship"]
///     </code>
///     <para>
///         <see cref="Send(TCommand, CancellationToken)" /> does <b>not</b> throw. It is called from an event handler, where an
///         exception has nowhere to go and would surface as an unhandled framework error rather than
///         as something the screen can show. The failure lands on <see cref="Error" /> and
///         <see cref="Status" />, which is where a component can actually render it. Use
///         <c>IQueryClient.Send</c> when you want the exception.
///     </para>
/// </remarks>
/// <typeparam name="TCommand">The command this dispatches.</typeparam>
public sealed class Command<TCommand>
    where TCommand : ICommand
{
    private readonly SessionQueryClient _client;
    private readonly CommandCore _core;
    private TCommand? _variables;

    internal Command(SessionQueryClient client)
    {
        _client = client;
        _core = new CommandCore();
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

    /// <summary>
    ///     The command last sent — set as it is sent, so a pending render can say what is in flight
    ///     ("Shipping #7…"), and kept afterwards; <c>default</c> until the first send and after a reset.
    /// </summary>
    public TCommand? Variables
    {
        get
        {
            _core.Observe();
            return _variables;
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

    /// <summary>Never sent, or <see cref="CommandStatus.Idle" /> again after a reset.</summary>
    public bool IsIdle => Status == CommandStatus.Idle;

    /// <summary>Running now. This is what disables the button.</summary>
    public bool IsPending => Status == CommandStatus.Pending;

    /// <summary>The last run succeeded.</summary>
    public bool IsSuccess => Status == CommandStatus.Success;

    /// <summary>The last run failed; see <see cref="Error" />.</summary>
    public bool IsError => Status == CommandStatus.Error;


    /// <summary>
    ///     Dispatches the command. Never throws — see the remarks on the type. Add
    ///     <c>.Optimistically(…)</c> to show the result before the server answers.
    /// </summary>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    public Dispatching Send(TCommand command, CancellationToken cancellationToken = default) =>
        new((edits, ct) => Run(command, edits, ct), [], cancellationToken);

    private Task Run(TCommand command, OptimisticEdit[] optimistic, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(optimistic);

        // Before the run, whose Pending render is the one that shows it.
        _variables = command;
        return _core.RunAsync<object?>(
            async ct =>
            {
                await _client.DispatchCommandAsync(command, ct).ConfigureAwait(false);
                return null;
            },
            () => _client.InvalidateDeclared(command),
            optimistic,
            cancellationToken);
    }

    /// <summary>Returns to <see cref="CommandStatus.Idle" />, clearing any error and what was sent.</summary>
    public void Reset()
    {
        _variables = default;
        _core.Reset();
    }
}

/// <summary>
///     A command that returns a value, rendered the same way as <see cref="Command{TCommand}" />.
/// </summary>
/// <typeparam name="TCommand">The command this dispatches.</typeparam>
/// <typeparam name="TResult">What the command returns.</typeparam>
public sealed class Command<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly SessionQueryClient _client;
    private readonly CommandCore _core;
    private TCommand? _variables;
    private TResult? _data;

    internal Command(SessionQueryClient client)
    {
        _client = client;
        _core = new CommandCore();
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
    /// <remarks>
    ///     Reading it registers the component like <see cref="Status" /> does, so a component that shows
    ///     only the result still re-renders when it lands.
    /// </remarks>
    public TResult? Data
    {
        get
        {
            _core.Observe();
            return _data;
        }
    }

    /// <summary>
    ///     The command last sent — set as it is sent, so a pending render can say what is in flight
    ///     ("Shipping #7…"), and kept afterwards; <c>default</c> until the first send and after a reset.
    /// </summary>
    public TCommand? Variables
    {
        get
        {
            _core.Observe();
            return _variables;
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

    /// <summary>Never sent, or <see cref="CommandStatus.Idle" /> again after a reset.</summary>
    public bool IsIdle => Status == CommandStatus.Idle;

    /// <summary>Running now. This is what disables the button.</summary>
    public bool IsPending => Status == CommandStatus.Pending;

    /// <summary>The last run succeeded.</summary>
    public bool IsSuccess => Status == CommandStatus.Success;

    /// <summary>The last run failed; see <see cref="Error" />.</summary>
    public bool IsError => Status == CommandStatus.Error;


    /// <summary>Dispatches the command. Never throws — the failure lands on <see cref="Error" />.</summary>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    /// <returns>What the command returned, or <c>default</c> if it failed.</returns>
    public Dispatching<TResult> Send(TCommand command, CancellationToken cancellationToken = default) =>
        new((edits, ct) => Run(command, edits, ct), [], cancellationToken);

    private Task<TResult?> Run(TCommand command, OptimisticEdit[] optimistic, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(optimistic);
        _variables = command;
        return _core.RunAsync(
            async ct =>
            {
                // Stored inside the run, BEFORE it reports success: the success notification re-renders
                // whoever is reading, and a render between the two would show Success with the old result.
                var result = await _client.DispatchCommandAsync(command, ct).ConfigureAwait(false);
                _data = result;
                return result;
            },
            () => _client.InvalidateDeclared(command),
            optimistic,
            cancellationToken);
    }

    /// <summary>Returns to <see cref="CommandStatus.Idle" />, clearing any error and result.</summary>
    public void Reset()
    {
        _data = default;
        _variables = default;
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
///           .OnClick(async () =&gt; await _ship.Send(ct =&gt; api.ShipAsync(id, ct)))
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
    private readonly SessionQueryClient _client;
    private readonly QueryKey[] _invalidates;
    private readonly CommandCore _core;

    internal Command(SessionQueryClient client, QueryKey[] invalidates)
    {
        _client = client;
        _invalidates = invalidates;
        _core = new CommandCore();
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

    /// <summary>Never sent, or <see cref="CommandStatus.Idle" /> again after a reset.</summary>
    public bool IsIdle => Status == CommandStatus.Idle;

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
    public Dispatching Send(Func<CancellationToken, Task> send, CancellationToken cancellationToken = default) =>
        new((edits, ct) => Run(send, edits, ct), [], cancellationToken);

    private Task Run(Func<CancellationToken, Task> send, OptimisticEdit[] optimistic, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(optimistic);
        return _core.RunAsync<object?>(
            async ct =>
            {
                await send(ct).ConfigureAwait(false);
                return null;
            },
            Invalidate,
            optimistic,
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
    public Dispatching<TResult> Send<TResult>(
        Func<CancellationToken, Task<TResult>> send,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(send);
        return new Dispatching<TResult>(
            (edits, ct) => _core.RunAsync(send, Invalidate, edits, ct), [], cancellationToken);
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
