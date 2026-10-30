using Rask.Cqrs;

namespace Rask.Querying.Tests;

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

    private int _cancelled;

    /// <summary>How many held queries were cancelled while they waited, so a test sees the work stop.</summary>
    public int Cancelled => Volatile.Read(ref _cancelled);

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
            try
            {
                await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelled);
                throw;
            }
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

    /// <summary>
    ///     When set, a command waits for it — so a test can look at a command while it is pending. The wait honours
    ///     the token the command was given, as a handler mid-write does, so a test that the caller's leaving does not
    ///     stop the command proves something.
    /// </summary>
    public TaskCompletionSource? CommandGate { get; set; }

    private int _commandsCompleted;

    /// <summary>How many commands ran to their end: the write a store would hold afterwards.</summary>
    public int CommandsCompleted => Volatile.Read(ref _commandsCompleted);

    public Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        CommandCount++;
        return Run<object?>(null, cancellationToken);
    }

    public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        CommandCount++;
        return Run((TResult)CommandResult!, cancellationToken);
    }

    private async Task<TResult> Run<TResult>(TResult result, CancellationToken cancellationToken)
    {
        if (CommandGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (ThrowOnCommand is { } thrown)
        {
            throw thrown;
        }

        Interlocked.Increment(ref _commandsCompleted);
        return result;
    }

    // Every open subscription, so a publish through this double reaches them the way the real feed would — minus
    // scopes, policies and replay, which the dispatcher's own tests cover.
    private readonly List<(Type Type, System.Threading.Channels.ChannelWriter<IEvent> Writer)> _subscribers = [];

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

    public Task Publish<TEvent>(
        TEvent notification,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
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

    public IAsyncEnumerable<TEvent> Subscribe<TEvent>(
        CancellationToken cancellationToken = default)
        where TEvent : IEvent =>
        Watch<TEvent>(null, cancellationToken);

    public IAsyncEnumerable<TEvent> Subscribe<TEvent>(
        ISubscription<TEvent> subscription,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent =>
        Watch(subscription, cancellationToken);

    private async IAsyncEnumerable<TEvent> Watch<TEvent>(
        ISubscription<TEvent>? subscription,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<IEvent>();
        var entry = (typeof(TEvent), channel.Writer);
        lock (_subscribers)
        {
            _subscribers.Add(entry);
        }

        try
        {
            await foreach (var notification in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var typed = (TEvent)notification;
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

/// <summary>
///     A clock whose timers fire only when the test moves it, so a poll is a step the test takes rather than a
///     wait it hopes is long enough.
/// </summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

    /// <summary>How many timers are waiting to fire — a cancelled wait takes its timer away.</summary>
    public int PendingTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(timer => timer.Due is not null);
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock on, firing every timer that falls due on the way.</summary>
    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += by;
            due = [.. _timers.Where(timer => timer.Due is { } at && at <= _now)];
            foreach (var timer in due)
            {
                timer.Due = null;
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private void Schedule(ManualTimer timer, TimeSpan dueTime)
    {
        lock (_gate)
        {
            timer.Due = dueTime == Timeout.InfiniteTimeSpan ? null : _now + dueTime;
        }
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            clock.Schedule(this, dueTime);
            return true;
        }

        public void Dispose() => clock.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
