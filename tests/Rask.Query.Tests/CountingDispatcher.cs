using Rask.Cqrs;

namespace Rask.Query.Tests;

/// <summary>A record query, so the cache key is the message itself by structural equality.</summary>
public sealed record GetOrders(int Page) : IQuery<string>;

public sealed record GetProfile(string User) : IQuery<string>;

[Invalidates(typeof(GetOrders))]
public sealed record ShipOrder(int Id) : ICommand;

public sealed record UnrelatedCommand(int Id) : ICommand;

/// <summary>Declares a key PREFIX rather than a message type.</summary>
[Invalidates("orders")]
public sealed record ArchiveEverything(int Id) : ICommand;

/// <summary>Declares both, which the attribute allows because it is AllowMultiple.</summary>
[Invalidates(typeof(GetProfile))]
[Invalidates("orders")]
public sealed record SweepingChange(int Id) : ICommand;

/// <summary>
///     A dispatcher that counts what it was asked to do and can be made to fail or to block, so a test
///     can observe deduplication and staleness rather than infer them from timing.
/// </summary>
internal sealed class CountingDispatcher : IDispatcher
{
    private TaskCompletionSource? _gate;

    private readonly Dictionary<Type, int> _perType = [];

    public int QueryCount { get; private set; }

    /// <summary>How many times one message type was dispatched, so a test that needs an unrelated
    /// query to trigger something is not counting that query too.</summary>
    public int QueryCountFor<TMessage>() => QueryCountFor(typeof(TMessage));

    private int QueryCountFor(Type message) => _perType.TryGetValue(message, out var n) ? n : 0;

    public int CommandCount { get; private set; }

    public string Result { get; set; } = "first";

    public Exception? Throw { get; set; }

    /// <summary>Fails this many times and then succeeds, so a retry can be seen to recover.</summary>
    public int FailTimes { get; set; }

    /// <summary>Makes the next command fail, so the rollback path can be asserted rather than assumed.</summary>
    public Exception? ThrowOnCommand { get; set; }

    /// <summary>What a value-returning command hands back.</summary>
    public object? CommandResult { get; set; } = 42;

    /// <summary>Holds the next dispatch until <see cref="Release" />, so an in-flight state is observable.</summary>
    public void Block() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _gate?.TrySetResult();

    public async Task<TResult> DispatchAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default)
    {
        QueryCount++;
        _perType[query.GetType()] = QueryCountFor(query.GetType()) + 1;
        if (_gate is { } gate)
        {
            // Honours the token, so a cancellation test proves something: awaiting the gate without
            // it would let a cancelled fetch run to completion anyway and the assertion would pass
            // for the wrong reason.
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (FailTimes > 0)
        {
            FailTimes--;
            throw Throw ?? new InvalidOperationException("transient");
        }

        if (Throw is { } error)
        {
            throw error;
        }

        return (TResult)(object)Result;
    }

    public Task DispatchAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        CommandCount++;
        return ThrowOnCommand is { } error ? Task.FromException(error) : Task.CompletedTask;
    }

    public Task<TResult> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        CommandCount++;
        return ThrowOnCommand is { } error
            ? Task.FromException<TResult>(error)
            : Task.FromResult((TResult)(object)CommandResult!);
    }

    public Task PublishAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification => Task.CompletedTask;
}

/// <summary>
///     A clock the test moves by hand, so staleness is asserted rather than waited for. A tiny local
///     type instead of a package reference: TimeProvider needs one override, and adding a dependency
///     to central package management for that would cost more than it saves.
/// </summary>
internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
