using System.Diagnostics.CodeAnalysis;
using Rask.Cqrs;

namespace Rask.Query;

/// <summary>
///     The shared state machine behind both mutation shapes: pending/error/success, the components
///     watching it, and any optimistic edits to roll back.
/// </summary>
internal sealed class MutationCore(QueryClient client)
{
    private readonly List<IOptimisticUpdate> _optimistic = [];
    private readonly ComponentReaders _readers = new();

    public MutationStatus Status { get; private set; } = MutationStatus.Idle;

    public Exception? Error { get; private set; }

    public void Observe() => _readers.Observe();

    public void AddOptimistic(IOptimisticUpdate update) => _optimistic.Add(update);

    public void Reset()
    {
        Status = MutationStatus.Idle;
        Error = null;
        _readers.RenderAll();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Whatever the handler threw belongs on the mutation as Error, for the component "
                        + "to render. See the remarks on RunAsync for why it is not rethrown.")]
    public async Task<TResult?> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> dispatch,
        object command,
        CancellationToken cancellationToken)
    {
        Status = MutationStatus.Pending;
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
            Status = MutationStatus.Success;

            // The declared invalidation replaces the optimistic guess with what the server actually
            // holds, so there is nothing to undo on success.
            client.InvalidateDeclared(command);
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
            Status = MutationStatus.Error;
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
///     private readonly Mutation&lt;ShipOrder&gt; _ship = q.Mutation&lt;ShipOrder&gt;();
///
///     Button.Disabled(_ship.IsPending)
///           .OnClick(() =&gt; _ship.RunAsync(new ShipOrder(id)))
///           [_ship.IsPending ? "Shipping…" : "Ship"]
///     </code>
///     <para>
///         <see cref="RunAsync" /> does <b>not</b> throw. It is called from an event handler, where an
///         exception has nowhere to go and would surface as an unhandled framework error rather than
///         as something the screen can show. The failure lands on <see cref="Error" /> and
///         <see cref="Status" />, which is where a component can actually render it. Use
///         <c>IQueryClient.MutateAsync</c> when you want the exception.
///     </para>
/// </remarks>
/// <typeparam name="TCommand">The command this dispatches.</typeparam>
public sealed class Mutation<TCommand>
    where TCommand : ICommand
{
    private readonly QueryClient _client;
    private readonly MutationCore _core;

    internal Mutation(QueryClient client)
    {
        _client = client;
        _core = new MutationCore(client);
    }

    /// <summary>Where this mutation is in its lifecycle.</summary>
    public MutationStatus Status
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
    public bool IsPending => Status == MutationStatus.Pending;

    /// <summary>The last run succeeded.</summary>
    public bool IsSuccess => Status == MutationStatus.Success;

    /// <summary>The last run failed; see <see cref="Error" />.</summary>
    public bool IsError => Status == MutationStatus.Error;

    /// <summary>
    ///     Edits a cached query's result before the server answers, and puts it back if the command
    ///     fails.
    /// </summary>
    /// <remarks>
    ///     Register these once, when the mutation is created. On success the command's
    ///     <see cref="InvalidatesAttribute" /> refetches and replaces the guess with the truth; on
    ///     failure the previous value is restored, because a screen still showing the optimistic
    ///     result after a refused save is worse than never having shown it.
    /// </remarks>
    /// <typeparam name="TResult">The query's result type.</typeparam>
    /// <param name="query">The query whose cached result to edit.</param>
    /// <param name="update">Produces the optimistic result from the current one.</param>
    /// <returns>This mutation, for chaining.</returns>
    public Mutation<TCommand> Optimistic<TResult>(IQuery<TResult> query, Func<TResult, TResult> update)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(update);
        _core.AddOptimistic(new OptimisticUpdate<TResult>(query, update));
        return this;
    }

    /// <summary>Dispatches the command. Never throws — see the remarks on the type.</summary>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    public Task RunAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _core.RunAsync<object?>(
            async ct =>
            {
                await _client.DispatchCommandAsync(command, ct).ConfigureAwait(false);
                return null;
            },
            command,
            cancellationToken);
    }

    /// <summary>Returns to <see cref="MutationStatus.Idle" />, clearing any error.</summary>
    public void Reset() => _core.Reset();
}

/// <summary>
///     A command that returns a value, rendered the same way as <see cref="Mutation{TCommand}" />.
/// </summary>
/// <typeparam name="TCommand">The command this dispatches.</typeparam>
/// <typeparam name="TResult">What the command returns.</typeparam>
public sealed class Mutation<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly QueryClient _client;
    private readonly MutationCore _core;

    internal Mutation(QueryClient client)
    {
        _client = client;
        _core = new MutationCore(client);
    }

    /// <summary>Where this mutation is in its lifecycle.</summary>
    public MutationStatus Status
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
    public bool IsPending => Status == MutationStatus.Pending;

    /// <summary>The last run succeeded.</summary>
    public bool IsSuccess => Status == MutationStatus.Success;

    /// <summary>The last run failed; see <see cref="Error" />.</summary>
    public bool IsError => Status == MutationStatus.Error;

    /// <inheritdoc cref="Mutation{TCommand}.Optimistic{TQueryResult}" />
    /// <typeparam name="TQueryResult">The query's result type.</typeparam>
    /// <param name="query">The query whose cached result to edit.</param>
    /// <param name="update">Produces the optimistic result from the current one.</param>
    /// <returns>This mutation, for chaining.</returns>
    public Mutation<TCommand, TResult> Optimistic<TQueryResult>(
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
    public async Task<TResult?> RunAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Data = await _core
            .RunAsync(ct => _client.DispatchCommandAsync(command, ct), command, cancellationToken)
            .ConfigureAwait(false);
        return Data;
    }

    /// <summary>Returns to <see cref="MutationStatus.Idle" />, clearing any error and result.</summary>
    public void Reset()
    {
        Data = default;
        _core.Reset();
    }
}
