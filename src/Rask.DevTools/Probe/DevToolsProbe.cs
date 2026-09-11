using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Diagnostics.DevTools;
using Rask.Core.Live;
using Rask.Core.Routing;

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

    /// <summary>The feed for <paramref name="session" />, created on first use.</summary>
    internal DevToolsFeed For(LiveSessionBase session) => _feeds.GetOrCreateValue(session);

    /// <summary>The feed for <paramref name="session" /> if the devtools have seen it.</summary>
    internal bool TryGet(LiveSessionBase session, out DevToolsFeed feed) => _feeds.TryGetValue(session, out feed!);
}

/// <summary>
///     What the runtime reports to while the devtools are attached: the wire traffic of every inspected session.
/// </summary>
/// <remarks>
///     <para>
///         Installed into <see cref="RaskDevToolsHook.Probe" /> only where the devtools switch on — Development, with the
///         kit the panel is drawn with present — so every other process keeps the null the hook sites branch on.
///     </para>
///     <para>
///         The panel is a Rask page with sessions of its own. Those are never recorded: a panel that measured itself
///         would show its own re-renders as the inspected app's traffic, growing with every refresh it caused.
///     </para>
///     <para>
///         The component-level members are empty for now; the tree and render tabs fill them in.
///     </para>
/// </remarks>
internal sealed class DevToolsProbe(DevToolsFeeds feeds) : IRaskDevToolsProbe
{
    /// <summary>The path prefix of the devtools' own pages, whose sessions are never recorded.</summary>
    internal const string PanelPrefix = "/_rask-devtools";

    public void WalkStarted(LiveSessionBase session, bool publishOnly)
    {
    }

    public long ComponentRendering(Component component, RenderCause cause) => 0;

    public void ComponentRendered(Component component, long startTimestamp)
    {
    }

    public void ComponentWalked(Component component, long startTimestamp, int frameStart, int frameEnd)
    {
    }

    public void ComponentReplayed(Component component, int frameStart, int frameEnd)
    {
    }

    public bool ObserveThrow(Component component, Exception exception) => false;

    public void StateRequested(Component component)
    {
    }

    public long HandlerStarting(Component owner, string handlerId, JsonElement payload) => 0;

    public void HandlerEnded(Component owner, string handlerId, long startTimestamp, Exception? fault)
    {
    }

    public void TreeCommitted(
        Component root, IReadOnlyCollection<Component> aliveNow, IReadOnlyCollection<Component> alivePrev)
    {
    }

    public void DiffComputed(LiveSessionBase session, int opCount, bool usedDiff, long startTimestamp)
    {
        if (!IsPanel(session))
        {
            feeds.For(session).RecordDiff(opCount, usedDiff);
        }
    }

    public void FrameSent(LiveSessionBase session, int bytes)
    {
        if (!IsPanel(session))
        {
            feeds.For(session).RecordWire(DevToolsWireDirection.In, "frame", bytes, Stopwatch.GetTimestamp());
        }
    }

    public void FrameReceived(LiveSessionBase session, int bytes, JsonElement frame)
    {
        if (IsPanel(session))
        {
            return;
        }

        // The element is only valid for the duration of this call, so only the type string is kept.
        var kind = frame.ValueKind == JsonValueKind.Object
                   && frame.TryGetProperty("type", out var type)
                   && type.ValueKind == JsonValueKind.String
            ? type.GetString() ?? "?"
            : "?";
        feeds.For(session).RecordWire(DevToolsWireDirection.Out, kind, bytes, Stopwatch.GetTimestamp());
    }

    private static bool IsPanel(LiveSessionBase session)
    {
        var path = session.Services.GetService<RouteState>()?.Path;
        return path is not null
               && path.StartsWith(PanelPrefix, StringComparison.OrdinalIgnoreCase)
               && (path.Length == PanelPrefix.Length || path[PanelPrefix.Length] == '/');
    }
}
