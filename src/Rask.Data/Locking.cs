using System.ComponentModel;
using System.Runtime.CompilerServices;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     A <c>Db.Lock</c> still being worded: <c>await Db.Lock("pump:7").For(30.Seconds)</c>. Nothing is taken until it is
///     awaited.
/// </summary>
/// <remarks>
///     Without <see cref="For" /> it waits for another holder as long as it takes — until the call is cancelled — as a
///     lock does that names no timeout.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Locking
{
    private readonly string _name;
    private readonly TimeSpan? _wait;
    private readonly CancellationToken _cancellationToken;

    internal Locking(string name, TimeSpan? wait, CancellationToken cancellationToken)
    {
        _name = name;
        _wait = wait;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    ///     Waits at most <paramref name="wait" /> for another holder to let go: <c>.For(30.Seconds)</c>; <c>.For(TimeSpan.Zero)</c>
    ///     does not wait at all. A wait that runs out throws <see cref="TimeoutException" />.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="wait" /> is negative.</exception>
    public Locking For(TimeSpan wait)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero);
        return new Locking(_name, wait, _cancellationToken);
    }

    /// <summary>Takes it.</summary>
    public TaskAwaiter<IAsyncDisposable> GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Takes it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable<IAsyncDisposable> ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Takes it, as a <see cref="Task{TResult}" />.</summary>
    /// <remarks>Inside an operation's dry run (what a form asks while it is filled in) nothing is taken.</remarks>
    public Task<IAsyncDisposable> AsTask() =>
        DryRun.IsRunning ? Task.FromResult(DryRun.NoLock) : NamedLock.Take(_name ?? throw new InvalidOperationException("Start a lock with Db.Lock(name)."), _wait, Ambient.Or(_cancellationToken));
}
