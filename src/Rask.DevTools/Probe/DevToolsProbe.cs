using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Diagnostics.DevTools;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.DevTools.Panel;

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

/// <summary>
///     What the runtime reports to while the devtools are attached: the wire traffic of every inspected session.
/// </summary>
/// <remarks>
///     <para>
///         Installed into <see cref="RaskDevToolsHook.Probe" /> only where the devtools switch on — Development on a Server
///         host, a page served from this machine on a WASM one, and the kit the panel is drawn with present — so every
///         other process keeps the null the hook sites branch on.
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

    // The session a render walk belongs to, found later by the component-level hooks. TreeCommitted and its neighbours
    // carry a component and no session, and the only thing that ties one to the other is the render context's services —
    // which on every host IS the session's container. Weak on both sides: neither the container nor the session is ours.
    private readonly ConditionalWeakTable<IServiceProvider, LiveSessionBase> _walking = new();

    // Holds the component ids, so a panel's expanded branches survive the next render of the page it is watching.
    private readonly DevToolsTreeSnapshotter _snapshots = new();

    public void WalkStarted(LiveSessionBase session, bool publishOnly)
    {
        if (!IsPanel(session))
        {
            _walking.AddOrUpdate(session.Services, session);
        }
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
        // Which session this walk belongs to: the render context's services are the session's container, and WalkStarted
        // recorded the pair. The synchronous context first — it is valid for the walk itself — with the ambient one as
        // the fallback for a render that resumed after an await.
        var services = (LiveRenderContext.CurrentSync ?? LiveRenderContext.Current)?.Services;
        if (services is null
            || !_walking.TryGetValue(services, out var session)
            || !feeds.TryGet(session, out var feed)
            || !feed.WantsTree)
        {
            return;
        }

        feed.RecordTree(_snapshots.Snapshot(root));
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

    // By type first: a WASM panel session shares the page's runtime, and its route state is its own container's, so the
    // type is what says it is the panel. A Server panel is an ordinary session, known by the path its page was served at.
    private static bool IsPanel(LiveSessionBase session)
    {
        if (session is DevToolsPanelSession)
        {
            return true;
        }

        var path = session.Services.GetService<RouteState>()?.Path;
        return path is not null
               && path.StartsWith(PanelPrefix, StringComparison.OrdinalIgnoreCase)
               && (path.Length == PanelPrefix.Length || path[PanelPrefix.Length] == '/');
    }
}
