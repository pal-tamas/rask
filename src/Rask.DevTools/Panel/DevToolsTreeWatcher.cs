using Rask.Core;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     Holds the inspected session's tree watch for as long as the panel is open, whichever tab is showing.
/// </summary>
/// <remarks>
///     A component rather than a field on the page, so the watch is taken and given back by the lifecycle: keyed by the
///     session, a panel that ever inspects a different one unmounts this watcher and mounts a fresh one, and a panel that
///     closes gives the watch back when its session is disposed. Renders nothing.
/// </remarks>
internal sealed partial class DevToolsTreeWatcher : Component
{
    private IDisposable? _watch;

    /// <summary>The inspected session's feed.</summary>
    public required DevToolsFeed Feed { get; set; }

    /// <inheritdoc />
    protected override Task OnMount()
    {
        _watch = Feed.WatchTree();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnUnmount()
    {
        _watch?.Dispose();
        _watch = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Component? Render() => null;
}
