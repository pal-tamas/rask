namespace Rask.Server.Diagnostics;

/// <summary>Why a live session left the store — the line an operator reads when a tab came back to nothing.</summary>
internal enum SessionRemoval
{
    /// <summary>No connection attached within <c>UnconnectedSessionGracePeriod</c> of its page being served.</summary>
    Unconnected,

    /// <summary>Its connection went and none came back within <c>SessionGracePeriod</c>.</summary>
    Detached,

    /// <summary>The tab said it was leaving.</summary>
    Left,

    /// <summary>Its page redirected while it was served, so nothing would ever connect to it.</summary>
    Redirected,

    /// <summary>The host is shutting down.</summary>
    Shutdown,

    /// <summary>The host ended it for its own reasons: a page printed, a test.</summary>
    Ended,
}
