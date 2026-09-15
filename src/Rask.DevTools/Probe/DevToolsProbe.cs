using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Diagnostics;
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

/// <summary>
///     What the runtime reports to while the devtools are attached: the wire traffic, component tree, renders, interaction
///     timings and errors of every inspected session.
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
///         <see cref="StateRequested" /> is empty: nothing shows a state request that did not lead to a render.
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

    // When the walk in progress started, for the Perf tab's render time.
    [ThreadStatic] private static long t_walkStart;

    // The feed of the session whose frame is being dispatched. The handler hooks carry a component and no session; both
    // hosts run the handler on the flow that received the frame (the Server chains it from the socket loop, WASM awaits it
    // in the same dispatch), so a value set when the frame arrives is the one the handler sees.
    private static readonly AsyncLocal<DevToolsFeed?> s_dispatching = new();

    // Stands in for a panel's own dispatch, so neither its handlers' timings nor their faults are recorded anywhere.
    private static readonly DevToolsFeed PanelDispatch = new();

    // The containers of the devtools' own sessions, so a fault or a diagnostic from a panel's render is dropped rather
    // than listed as the app's.
    private readonly ConditionalWeakTable<IServiceProvider, object> _panels = new();

    // Every exception already listed, so the framework's own report of the same fault does not list it twice.
    private static readonly ConditionalWeakTable<Exception, object> s_listed = new();
    private static readonly object Listed = new();

    // The render fault this thread is unwinding, and its entry, which each enclosing component adds itself to.
    [ThreadStatic] private static Exception? t_unwinding;
    [ThreadStatic] private static DevToolsErrorLog.Entry? t_unwindingEntry;
    [ThreadStatic] private static DevToolsErrorLog? t_unwindingLog;

    // Holds the component ids, so a panel's expanded branches survive the next render of the page it is watching — and the
    // Renders tab names a component by the same id the Tree tab does.
    private readonly DevToolsTreeSnapshotter _snapshots = new();

    public void WalkStarted(LiveSessionBase session, bool publishOnly)
    {
        if (IsPanel(session))
        {
            _panels.AddOrUpdate(session.Services, Listed);
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
        t_walkStart = Stopwatch.GetTimestamp();
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

    public bool ObserveThrow(Component component, Exception exception)
    {
        // The filter runs once for every component the exception unwinds through, innermost first: the first sighting
        // records it, each later one is the next component out.
        if (ReferenceEquals(t_unwinding, exception) && t_unwindingEntry is { } entry)
        {
            t_unwindingLog!.Enclosing(entry, DevToolsNames.Of(component.GetType()));
            return false;
        }

        t_unwinding = exception;
        t_unwindingEntry = null;
        t_unwindingLog = null;
        if (LogForNow(out var appWide) is { } log && s_listed.TryAdd(exception, Listed))
        {
            var (title, message) = Describe(exception);
            t_unwindingLog = log;
            t_unwindingEntry = log.Record(
                DevToolsErrorKind.Render, isWarning: false, title, message, exception.ToString(),
                DevToolsNames.Of(component.GetType()), _snapshots.IdOf(component), caught: false, appWide,
                DateTimeOffset.Now);
        }

        return false;
    }

    public void ComponentFaulted(Component component, Exception exception, ErrorSource source, bool caught) =>
        RecordFault(component, exception, source == ErrorSource.Lifecycle ? DevToolsErrorKind.Lifecycle : DevToolsErrorKind.Handler,
            caught);

    public void DiagnosticReported(in RaskDiagnosticEvent diagnostic)
    {
        if (!DevToolsErrorLog.IsWorthListing(diagnostic.Level)
            || (diagnostic.Exception is { } listed && s_listed.TryGetValue(listed, out _))
            || LogForNow(out var appWide) is not { } log)
        {
            return;
        }

        var message = diagnostic.Exception is { } exception
            ? diagnostic.Message + ": " + Describe(exception).Message
            : diagnostic.Message;
        log.Record(
            DevToolsErrorKind.Diagnostic, diagnostic.Level == RaskLogLevel.Warning, diagnostic.Category, message,
            diagnostic.Exception?.ToString(), component: null, componentId: null, caught: false, appWide,
            DateTimeOffset.Now);
    }

    private void RecordFault(Component component, Exception exception, DevToolsErrorKind kind, bool caught)
    {
        if (!s_listed.TryAdd(exception, Listed) || LogForNow(out var appWide) is not { } log)
        {
            return;
        }

        var (title, message) = Describe(exception);
        var entry = log.Record(
            kind, isWarning: false, title, message, exception.ToString(), DevToolsNames.Of(component.GetType()),
            _snapshots.IdOf(component), caught, appWide, DateTimeOffset.Now);

        // The components around it, from the page's last render: a handler or a hook is not inside a walk to unwind.
        if (!appWide && s_dispatching.Value is { } feed && !ReferenceEquals(feed, PanelDispatch))
        {
            foreach (var ancestor in feed.AncestorsOf(component))
            {
                log.Enclosing(entry, ancestor);
            }
        }
    }

    // Which log a report made right now belongs in: the page whose frame is being dispatched, the page being rendered, or —
    // outside both — the app-wide one. Null for the devtools' own sessions, whose errors are not the app's.
    private DevToolsErrorLog? LogForNow(out bool appWide)
    {
        appWide = false;
        if (s_dispatching.Value is { } dispatching)
        {
            return ReferenceEquals(dispatching, PanelDispatch) ? null : dispatching.Errors;
        }

        if ((LiveRenderContext.CurrentSync ?? LiveRenderContext.Current)?.Services is { } services)
        {
            if (_panels.TryGetValue(services, out _))
            {
                return null;
            }

            if (_walking.TryGetValue(services, out var session))
            {
                return feeds.For(session).Errors;
            }
        }

        appWide = true;
        return feeds.AppWide;
    }

    // The exception a developer means: the one inside the reflection and task wrappers the runtime adds around it.
    internal static (string Title, string Message) Describe(Exception exception)
    {
        var inner = exception;
        while (inner is System.Reflection.TargetInvocationException or AggregateException && inner.InnerException is { } next)
        {
            inner = next;
        }

        return (inner.GetType().Name, inner.Message);
    }

    /// <summary>Marks the flow a panel's own event runs on, so nothing its handlers do is recorded as the app's.</summary>
    internal static void EnterPanelDispatch() => s_dispatching.Value = PanelDispatch;

    public void StateRequested(Component component)
    {
    }

    public long HandlerStarting(Component owner, string handlerId, JsonElement payload)
    {
        if (s_dispatching.Value is not { } feed || ReferenceEquals(feed, PanelDispatch))
        {
            return 0;
        }

        var now = Stopwatch.GetTimestamp();
        feed.PerfHandlerStarted(DevToolsNames.Of(owner.GetType()), now);
        return now;
    }

    public void HandlerEnded(Component owner, string handlerId, long startTimestamp, Exception? fault)
    {
        if (startTimestamp != 0 && s_dispatching.Value is { } feed && !ReferenceEquals(feed, PanelDispatch))
        {
            feed.PerfHandlerEnded(startTimestamp, Stopwatch.GetTimestamp(), fault is not null);
        }

        // A fault here is one no boundary took; a caught one arrived through ComponentFaulted already.
        if (fault is not null)
        {
            RecordFault(owner, fault, DevToolsErrorKind.Handler, caught: false);
        }
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
        feed.PerfWalk(t_walkStart, Stopwatch.GetTimestamp());
        if (renders is not null)
        {
            feed.RecordCommit(
                renders, Distinct(walk, renders), _snapshots, Stopwatch.GetTimestamp(), walk, FrameSinkScope.Current);
        }

        // Kept whether or not a panel is watching, so one that opens before the page renders again still has a tree. The
        // frame writer is still the walk's: the session pops it only after this returns.
        feed.RecordWalk(root, walk, FrameSinkScope.Current, session.DevToolsRenderGate, _snapshots);
    }

    public void DiffComputed(LiveSessionBase session, int opCount, bool usedDiff, long startTimestamp)
    {
        if (!IsPanel(session))
        {
            var feed = feeds.For(session);
            feed.RecordDiff(opCount, usedDiff);
            if (startTimestamp != 0)
            {
                feed.PerfDiff(startTimestamp, Stopwatch.GetTimestamp());
            }
        }
    }

    public void FrameSent(LiveSessionBase session, int bytes)
    {
        if (!IsPanel(session))
        {
            var feed = feeds.For(session);
            var now = Stopwatch.GetTimestamp();
            feed.RecordWire(DevToolsWireDirection.In, "frame", bytes, now);
            feed.PerfFrameSent(bytes, now);
        }
    }

    public void FrameReceived(LiveSessionBase session, int bytes, JsonElement frame)
    {
        if (IsPanel(session))
        {
            // The panel's own handlers are not the app's interactions, nor are their faults the app's errors.
            s_dispatching.Value = PanelDispatch;
            return;
        }

        // The element is only valid for the duration of this call, so only the type string is kept.
        var kind = frame.ValueKind == JsonValueKind.Object
                   && frame.TryGetProperty("type", out var type)
                   && type.ValueKind == JsonValueKind.String
            ? type.GetString() ?? "?"
            : "?";
        var feed = feeds.For(session);
        var now = Stopwatch.GetTimestamp();
        feed.RecordWire(DevToolsWireDirection.Out, kind, bytes, now);
        feed.PerfInbound(kind, now);
        s_dispatching.Value = feed;
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
