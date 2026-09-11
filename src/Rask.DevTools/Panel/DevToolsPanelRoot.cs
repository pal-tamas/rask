namespace Rask.DevTools.Panel;

/// <summary>
///     Provided by <see cref="DevToolsShell" /> to everything it renders, so a panel page can tell that it is rendering in
///     the panel's own document.
/// </summary>
/// <remarks>
///     <para>
///         The panel's admission — Development, this machine, the page's origin, the session's token and owner — runs when
///         the host's page handler picks the panel's mount for a request. A live session skips that: every router matches
///         every mounted application's pages, so an app session can navigate itself onto <c>/_rask-devtools</c> over its
///         own socket and render a panel page inside the app's document without passing the loopback or origin check.
///     </para>
///     <para>
///         Only <see cref="DevToolsShell" /> provides this, and only a request the admission let through gets that shell as
///         its root. <see cref="DevToolsLayout" /> renders nothing of the panel without it, so the tabs never run anywhere
///         else.
///     </para>
/// </remarks>
internal sealed class DevToolsPanelRoot
{
    /// <summary>The one value the shell provides; nothing about it varies.</summary>
    internal static readonly DevToolsPanelRoot Instance = new();

    private DevToolsPanelRoot()
    {
    }
}
