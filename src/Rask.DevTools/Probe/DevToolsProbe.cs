using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
///     What the runtime reports to while the devtools are attached: the wire traffic, component tree and renders of every
///     inspected session.
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
///         The handler and state members are empty for now; the perf and errors tabs fill them in.
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

    // The walk in progress on this thread: every component it finished, with the component it was walked inside. A render
    // walk is synchronous from WalkStarted to TreeCommitted, so the thread is what ties the two together; the list is
    // reused, so a page that renders on every keystroke does not allocate one per render. Null outside a session's walk —
    // a prerender, a ToHtml — which is recorded nowhere.
    [ThreadStatic] private static List<DevToolsWalkItem>? t_walk;
    [ThreadStatic] private static List<DevToolsWalkItem>? t_walkBuffer;

    // The components whose Render() ran in that walk, beside it and reset with it. Every other component the walk passed
    // was served from its render cache.
    [ThreadStatic] private static List<DevToolsRenderItem>? t_renders;
    [ThreadStatic] private static List<DevToolsRenderItem>? t_rendersBuffer;
    [ThreadStatic] private static HashSet<Component>? t_distinct;

    // Holds the component ids, so a panel's expanded branches survive the next render of the page it is watching — and the
    // Renders tab names a component by the same id the Tree tab does.
    private readonly DevToolsTreeSnapshotter _snapshots = new();

    public void WalkStarted(LiveSessionBase session, bool publishOnly)
    {
        if (IsPanel(session))
        {
            t_walk = null;
            t_renders = null;
            return;
        }

        _walking.AddOrUpdate(session.Services, session);
        var buffer = t_walkBuffer ??= [];
        buffer.Clear();
        t_walk = buffer;
        var renders = t_rendersBuffer ??= [];
        renders.Clear();
        t_renders = renders;
    }

    public long ComponentRendering(Component component, RenderCause cause)
    {
        if (t_renders is not { } renders)
        {
            return 0;
        }

        renders.Add(new DevToolsRenderItem(component, cause, SelfTicks: -1));
        return Stopwatch.GetTimestamp();
    }

    public void ComponentRendered(Component component, long startTimestamp)
    {
        if (t_renders is not { } renders || startTimestamp == 0)
        {
            return;
        }

        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;

        // Render() builds markup and returns; its children render later, when the walk reaches them, so the render this
        // closes is the newest one — searched for rather than assumed, in case a component renders another inside its own.
        var items = CollectionsMarshal.AsSpan(renders);
        for (var i = items.Length - 1; i >= 0; i--)
        {
            if (ReferenceEquals(items[i].Component, component) && items[i].SelfTicks < 0)
            {
                items[i].SelfTicks = elapsed;
                return;
            }
        }
    }

    public void ComponentWalked(Component component, Component? parent, long startTimestamp, int frameStart, int frameEnd) =>
        t_walk?.Add(new DevToolsWalkItem(component, parent, frameStart, frameEnd));

    public void ComponentReplayed(Component component, Component? parent, int frameStart, int frameEnd) =>
        t_walk?.Add(new DevToolsWalkItem(component, parent, frameStart, frameEnd));

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
        var walk = t_walk;
        var renders = t_renders;
        t_walk = null;
        t_renders = null;

        var services = (LiveRenderContext.CurrentSync ?? LiveRenderContext.Current)?.Services;
        if (walk is null || services is null || !_walking.TryGetValue(services, out var session))
        {
            return;
        }

        var feed = feeds.For(session);
        if (renders is not null)
        {
            feed.RecordCommit(renders, Distinct(walk, renders), _snapshots, Stopwatch.GetTimestamp());
        }

        // Kept whether or not a panel is watching, so one that opens before the page renders again still has a tree. The
        // frame writer is still the walk's: the session pops it only after this returns.
        feed.RecordWalk(root, walk, FrameSinkScope.Current, session.DevToolsRenderGate, _snapshots);
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

    // How many components the commit went through. Not the walk alone: a component the serializer renders on a path of its
    // own — an error boundary — runs Render() without being reported as walked.
    private static int Distinct(List<DevToolsWalkItem> walk, List<DevToolsRenderItem> renders)
    {
        var seen = t_distinct ??= new HashSet<Component>(ReferenceEqualityComparer.Instance);
        seen.Clear();
        foreach (var item in walk)
        {
            seen.Add(item.Component);
        }

        foreach (var item in renders)
        {
            seen.Add(item.Component);
        }

        var count = seen.Count;
        // Emptied, not kept: it would otherwise hold the page's components alive until this thread's next commit.
        seen.Clear();
        return count;
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
