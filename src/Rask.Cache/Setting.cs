using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Caching;

/// <summary>
///     A <c>Set</c> still being worded: <c>await Cache.Set("banner", text).Until(midnight)</c>. Nothing is
///     written until it is awaited.
/// </summary>
/// <typeparam name="T">What is stored.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Setting<T>
{
    private readonly ICache? _cache;
    private readonly string _key;
    private readonly T _value;
    private readonly CacheLifetime _lifetime;
    private readonly CancellationToken _cancellationToken;

    internal Setting(ICache? cache, string key, T value, CacheLifetime lifetime, CancellationToken cancellationToken)
    {
        _cache = cache;
        _key = key;
        _value = value;
        _lifetime = lifetime;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Keeps the value for <paramref name="duration" /> from now: <c>.For(10.Minutes)</c>.</summary>
    public Setting<T> For(TimeSpan duration) =>
        With(_lifetime with { For = CacheLifetime.Positive(duration, nameof(For)) });

    /// <summary>Keeps the value while it is read at least every <paramref name="window" />: <c>.Sliding(20.Minutes)</c>.</summary>
    public Setting<T> Sliding(TimeSpan window) =>
        With(_lifetime with { Sliding = CacheLifetime.Positive(window, nameof(Sliding)) });

    /// <summary>Keeps the value until <paramref name="moment" />: <c>.Until(midnight)</c>.</summary>
    public Setting<T> Until(DateTimeOffset moment) => With(_lifetime with { Until = moment });

    /// <summary>Runs it.</summary>
    public TaskAwaiter GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task" />.</summary>
    public Task AsTask() => (_cache ?? Cache.Resolve()).Set(_key, _value, _lifetime, Ambient.Or(_cancellationToken));

    private Setting<T> With(CacheLifetime lifetime) => new(_cache, _key, _value, lifetime, _cancellationToken);
}
