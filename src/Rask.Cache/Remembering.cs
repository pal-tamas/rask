using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Caching;

/// <summary>
///     A <c>Remember</c> still being worded: <c>await Cache.Remember("products", Load).For(10.Minutes)</c>.
///     Nothing is read or loaded until it is awaited.
/// </summary>
/// <typeparam name="T">What is remembered.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Remembering<T>
{
    private readonly ICache? _cache;
    private readonly string _key;
    private readonly Func<CancellationToken, Task<T>> _load;
    private readonly CacheLifetime _lifetime;
    private readonly CancellationToken _cancellationToken;

    internal Remembering(
        ICache? cache, string key, Func<CancellationToken, Task<T>> load, CacheLifetime lifetime, CancellationToken cancellationToken)
    {
        _cache = cache;
        _key = key;
        _load = load;
        _lifetime = lifetime;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Keeps the value for <paramref name="duration" /> from now: <c>.For(10.Minutes)</c>.</summary>
    public Remembering<T> For(TimeSpan duration) =>
        With(_lifetime with { For = CacheLifetime.Positive(duration, nameof(For)) });

    /// <summary>Keeps the value while it is read at least every <paramref name="window" />: <c>.Sliding(20.Minutes)</c>.</summary>
    public Remembering<T> Sliding(TimeSpan window) =>
        With(_lifetime with { Sliding = CacheLifetime.Positive(window, nameof(Sliding)) });

    /// <summary>Keeps the value until <paramref name="moment" />: <c>.Until(midnight)</c>.</summary>
    public Remembering<T> Until(DateTimeOffset moment) => With(_lifetime with { Until = moment });

    /// <summary>Runs it.</summary>
    public TaskAwaiter<T> GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable<T> ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task{TResult}" />.</summary>
    public Task<T> AsTask() =>
        (_cache ?? Cache.Resolve()).Remember(_key, _load, _lifetime, Ambient.Or(_cancellationToken));

    private Remembering<T> With(CacheLifetime lifetime) => new(_cache, _key, _load, lifetime, _cancellationToken);
}
