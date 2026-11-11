using Microsoft.Extensions.Caching.Hybrid;
using Rask.Core.Caching;

namespace Rask.Server.Caching;

/// <summary>
///     Keeps cached fragments in the app's own <see cref="HybridCache" />, so every server behind it shares them and a
///     tag forgotten on one is forgotten on all.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="HybridCache" /> has an absolute expiration and no sliding one, so a sliding fragment is stored
///         with its window as the expiration and stored again — moved on — when a read finds half of it spent. The
///         absolute deadline travels with the markup, so no refresh can carry an entry past it.
///     </para>
///     <para>
///         A read never writes the miss back (<see cref="HybridCacheEntryFlags.DisableLocalCacheWrite" />,
///         <see cref="HybridCacheEntryFlags.DisableDistributedCacheWrite" />): only a settled render stores.
///     </para>
/// </remarks>
internal sealed class HybridFragmentStore(HybridCache cache, CachedOptions options, TimeProvider time) : IFragmentStore
{
    private static readonly HybridCacheEntryOptions _readOnly = new()
    {
        Flags = HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite
    };

    public CachedOptions Options => options;

    public async ValueTask<string?> Get(string key, CancellationToken cancellationToken = default)
    {
        var stored = await cache.GetOrCreateAsync<string?>(
                key, static _ => ValueTask.FromResult<string?>(null), _readOnly, tags: null, cancellationToken)
            .ConfigureAwait(false);
        if (stored is null || !FragmentEnvelope.TryOpen(stored, out var envelope))
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (now >= envelope.AbsoluteEnd)
        {
            return null;
        }

        if (envelope.Sliding is { } window && now - envelope.ReadAt >= window / 2)
        {
            await Put(key, envelope with { ReadAt = now }, envelope.Tags, now, cancellationToken).ConfigureAwait(false);
        }

        return envelope.Html;
    }

    public async ValueTask Set(string key, string html, FragmentLifetime lifetime, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        if ((long)html.Length * sizeof(char) > options.MaxEntrySize)
        {
            return;
        }

        var now = time.GetUtcNow();
        var envelope = new FragmentEnvelope(html, now + lifetime.Absolute, lifetime.Sliding, now, [.. tags]);
        await Put(key, envelope, envelope.Tags, now, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask Forget(string tag, CancellationToken cancellationToken = default) =>
        cache.RemoveByTagAsync(tag, cancellationToken);

    // The expiration is what is left of the absolute lifetime, or of the sliding window when that is shorter.
    private ValueTask Put(string key, FragmentEnvelope envelope, string[] tags, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var left = envelope.AbsoluteEnd - now;
        if (envelope.Sliding is { } window && window < left)
        {
            left = window;
        }

        var entry = new HybridCacheEntryOptions { Expiration = left, LocalCacheExpiration = left };
        return cache.SetAsync(key, envelope.Seal(), entry, tags, cancellationToken);
    }
}
