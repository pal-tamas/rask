namespace Rask.Core.Caching;

/// <summary>
///     Where <see cref="Components.Cached" /> keeps the markup it drew, across requests and sessions.
/// </summary>
/// <remarks>
///     <para>
///         The server registers one that writes through the app's <c>HybridCache</c> when it has one and keeps an
///         in-process <see cref="MemoryFragmentStore" /> otherwise; the browser, which has no other requests to share
///         with, uses the in-process one.
///     </para>
///     <para>
///         <see cref="Get" /> answers at once for an entry held in this process — a <see cref="ValueTask{T}" /> already
///         completed — so the common hit costs no extra render wave.
///     </para>
/// </remarks>
internal interface IFragmentStore
{
    /// <summary>The bounds and default lifetime it was set up with.</summary>
    CachedOptions Options { get; }

    /// <summary>The markup stored under <paramref name="key" />, or null when there is none or it has expired.</summary>
    ValueTask<string?> Get(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="html" /> under <paramref name="key" /> for <paramref name="lifetime" />, findable by each of <paramref name="tags" />.</summary>
    ValueTask Set(string key, string html, FragmentLifetime lifetime, IReadOnlyList<string> tags, CancellationToken cancellationToken = default);

    /// <summary>Removes every entry stored with <paramref name="tag" />.</summary>
    ValueTask Forget(string tag, CancellationToken cancellationToken = default);
}
