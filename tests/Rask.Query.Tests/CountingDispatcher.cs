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

    // Polling ticks dispatch from thread-pool threads while the test reads the counts, so they are kept under
    // a lock: a lost increment in the double would be a flake that says nothing about the code under test.
    private readonly Lock _counts = new();
    private readonly Dictionary<Type, int> _perType = [];
    private int _queryCount;

    public int QueryCount
    {
        get
        {
            lock (_counts)
            {
                return _queryCount;
            }
        }
    }

    /// <summary>How many times one message type was dispatched, so a test that needs an unrelated
    /// query to trigger something is not counting that query too.</summary>
    public int QueryCountFor<TMessage>() => QueryCountFor(typeof(TMessage));

    private int QueryCountFor(Type message)
    {
        lock (_counts)
        {
            return _perType.TryGetValue(message, out var n) ? n : 0;
        }
    }

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

    public async Task<TResult> Query<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default)
    {
        lock (_counts)
        {
            _queryCount++;
            _perType[query.GetType()] = _perType.TryGetValue(query.GetType(), out var n) ? n + 1 : 1;
        }
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

    /// <summary>When set, a void command waits for it — so a test can look at a command while it is pending.</summary>
    public TaskCompletionSource? CommandGate { get; set; }

    public Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        CommandCount++;
        if (CommandGate is { } gate)
        {
            return Gated(gate.Task);
        }

        return ThrowOnCommand is { } error ? Task.FromException(error) : Task.CompletedTask;

        async Task Gated(Task open)
        {
            await open.ConfigureAwait(false);
            if (ThrowOnCommand is { } thrown)
            {
                throw thrown;
            }
        }
    }

    public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        CommandCount++;
        return ThrowOnCommand is { } error
            ? Task.FromException<TResult>(error)
            : Task.FromResult((TResult)(object)CommandResult!);
    }

    // Every open subscription, so a publish through this double reaches them the way the real feed would — minus
    // scopes, policies and replay, which the dispatcher's own tests cover.
    private readonly List<(Type Type, System.Threading.Channels.ChannelWriter<INotification> Writer)> _subscribers = [];

    /// <summary>How many subscriptions are open right now, so a test can see one close.</summary>
    public int SubscriberCount
    {
        get
        {
            lock (_subscribers)
            {
                return _subscribers.Count;
            }
        }
    }

    public Task Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        lock (_subscribers)
        {
            foreach (var (type, writer) in _subscribers)
            {
                if (type == notification.GetType())
                {
                    writer.TryWrite(notification);
                }
            }
        }

        return Task.CompletedTask;
    }

    public IAsyncEnumerable<TNotification> Subscribe<TNotification>(
        CancellationToken cancellationToken = default)
        where TNotification : INotification =>
        Watch<TNotification>(null, cancellationToken);

    public IAsyncEnumerable<TNotification> Subscribe<TNotification>(
        ISubscription<TNotification> subscription,
        CancellationToken cancellationToken = default)
        where TNotification : INotification =>
        Watch(subscription, cancellationToken);

    private async IAsyncEnumerable<TNotification> Watch<TNotification>(
        ISubscription<TNotification>? subscription,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<INotification>();
        var entry = (typeof(TNotification), channel.Writer);
        lock (_subscribers)
        {
            _subscribers.Add(entry);
        }

        try
        {
            await foreach (var notification in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var typed = (TNotification)notification;
                if (subscription is null || subscription.Matches(typed))
                {
                    yield return typed;
                }
            }
        }
        finally
        {
            lock (_subscribers)
            {
                _subscribers.Remove(entry);
            }
        }
    }
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
