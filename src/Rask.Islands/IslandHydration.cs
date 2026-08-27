namespace Rask.Islands;

/// <summary>
///     When an island's adapter mounts in the browser.
/// </summary>
/// <remarks>
///     Server rendering is unaffected — the host element and its props are always in the first reply.
///     What this schedules is the moment the foreign renderer takes ownership of the subtree.
/// </remarks>
public enum IslandHydration
{
    /// <summary>As soon as the island's chunk has loaded. The default.</summary>
    Load = 0,

    /// <summary>On <c>requestIdleCallback</c>, so it yields to anything the page is still doing.</summary>
    Idle = 1,

    /// <summary>
    ///     On <c>IntersectionObserver</c>: the chunk is not even fetched until the island is scrolled
    ///     into view.
    /// </summary>
    Visible = 2,

    /// <summary>
    ///     Never. The island ships no JavaScript and stays exactly as the server rendered it.
    /// </summary>
    /// <remarks>
    ///     Only useful once the island's markup can be produced without a browser, so today this means
    ///     an island that renders nothing but its own children. Server-side rendering for the
    ///     bundler-backed runtimes is a later phase; until then prefer a server-rendered fallback the
    ///     adapter replaces on mount.
    /// </remarks>
    None = 3,
}
