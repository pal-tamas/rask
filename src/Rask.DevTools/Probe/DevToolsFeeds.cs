using System.Runtime.CompilerServices;
using Rask.Core.Live;

namespace Rask.DevTools.Probe;

/// <summary>
///     The feeds, one per inspected session, found by the session itself.
/// </summary>
/// <remarks>
///     Keyed weakly on the session: the session store raises nothing when a session ends, and a feed that outlived its
///     session would be a slow leak in the one process a developer leaves running all day. When the session is collected,
///     so is its feed.
/// </remarks>
internal sealed class DevToolsFeeds
{
    private readonly ConditionalWeakTable<LiveSessionBase, DevToolsFeed> _feeds = new();

    /// <summary>
    ///     What the framework reported outside any page's render or handler — at startup, from a background service. Every
    ///     panel shows it under its app-wide filter.
    /// </summary>
    internal DevToolsErrorLog AppWide { get; } = new();

    // Weak for the same reason as the table. Written on every record; a torn read between two sessions is harmless,
    // because the browser host that reads it has one app session.
    private readonly WeakReference<LiveSessionBase?> _latest = new(null);

    /// <summary>The feed for <paramref name="session" />, created on first use.</summary>
    internal DevToolsFeed For(LiveSessionBase session)
    {
        _latest.SetTarget(session);
        return _feeds.GetOrCreateValue(session);
    }

    /// <summary>The feed for <paramref name="session" /> if the devtools have seen it.</summary>
    internal bool TryGet(LiveSessionBase session, out DevToolsFeed feed) => _feeds.TryGetValue(session, out feed!);

    /// <summary>
    ///     The feed of the session recorded most recently, or null before any. A WASM page has one app session, so this is
    ///     the one its panel inspects; a Server host names the session instead.
    /// </summary>
    internal DevToolsFeed? Latest => _latest.TryGetTarget(out var session) && session is not null
        ? _feeds.GetOrCreateValue(session)
        : null;
}
