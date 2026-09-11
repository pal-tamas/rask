using System.Text.Json;
using Rask.Core.Live;

namespace Rask.Core.Diagnostics.DevTools;

/// <summary>
///     Why a component's <c>Render()</c> actually ran, rather than being served from its cache. Checked in declaration
///     order; the first that applies is the cause.
/// </summary>
/// <remarks>
///     There is deliberately no <c>Mount</c>. "Nothing cached" is not the same as "first render": a live session captures a
///     clean pure-element subtree as frames and drops the component's cached result, and a component that renders null
///     never has one. Reporting those as mounts would call every such re-render a mount. The devtools recognise a mount as
///     the first time they see a component render.
/// </remarks>
internal enum RenderCause : byte
{
    /// <summary>A parent passed changed props.</summary>
    Props,

    /// <summary><c>StateHasChanged</c>, or a handler that marked the component dirty.</summary>
    State,

    /// <summary>The component opts out of the render cache (<c>BypassRenderCache</c>).</summary>
    Forced,

    /// <summary>It read ambient state (context, culture) during an earlier render.</summary>
    AmbientState,

    /// <summary>A non-element component with children, whose cache cannot be trusted.</summary>
    Children,

    /// <summary>
    ///     None of the above, so the render cache was empty: a first render, a subtree whose cache was captured as frames,
    ///     or a component that renders null.
    /// </summary>
    Uncached,
}

/// <summary>
///     What the render runtime tells Rask DevTools, when — and only when — a probe is attached.
/// </summary>
/// <remarks>
///     <para>
///         Every call site reads <see cref="RaskDevToolsHook.Active" /> once and calls nothing when it is
///         null. That is the whole cost of the seam on a page without the devtools: one static read and a
///         branch, no allocation, no timestamp. A Release publish trims further — the feature switch folds
///         <see cref="RaskDevToolsHook.Active" /> to null and the branches go with it.
///     </para>
///     <para>
///         Implementations run on the render thread, inside the walk. They must not throw, must not render,
///         and must copy anything they keep: a <see cref="JsonElement" /> or a frame span is only valid for the
///         duration of the call.
///     </para>
/// </remarks>
internal interface IRaskDevToolsProbe
{
    /// <summary>A session began a render walk.</summary>
    void WalkStarted(LiveSessionBase session, bool publishOnly);

    /// <summary>A component is about to run <c>Render()</c>. Returns a timestamp passed back to the pair.</summary>
    long ComponentRendering(Component component, RenderCause cause);

    /// <summary>A component's <c>Render()</c> returned (not called when it threw).</summary>
    void ComponentRendered(Component component, long startTimestamp);

    /// <summary>
    ///     A component and its whole subtree were serialized. <paramref name="frameStart" /> and
    ///     <paramref name="frameEnd" /> bracket what it wrote into the live frame stream, or are -1 when the
    ///     walk captured no frames.
    /// </summary>
    void ComponentWalked(Component component, long startTimestamp, int frameStart, int frameEnd);

    /// <summary>A clean component was replayed from its cached frames instead of being walked.</summary>
    void ComponentReplayed(Component component, int frameStart, int frameEnd);

    /// <summary>
    ///     A component's render or walk threw. Used as an exception filter, so it MUST return false: the
    ///     exception keeps propagating to the boundary that handles it, and the stack is not unwound here.
    /// </summary>
    bool ObserveThrow(Component component, Exception exception);

    /// <summary>A component asked to re-render (<c>StateHasChanged</c>).</summary>
    void StateRequested(Component component);

    /// <summary>A DOM event is about to run a handler. Returns a timestamp passed back to the pair.</summary>
    long HandlerStarting(Component owner, string handlerId, JsonElement payload);

    /// <summary>A handler finished, or faulted with <paramref name="fault" />.</summary>
    void HandlerEnded(Component owner, string handlerId, long startTimestamp, Exception? fault);

    /// <summary>A live root committed a render: the components alive now, and the ones alive before it.</summary>
    void TreeCommitted(Component root, IReadOnlyCollection<Component> aliveNow, IReadOnlyCollection<Component> alivePrev);

    /// <summary>A session decided between a diff and a full frame for the render it just walked.</summary>
    void DiffComputed(LiveSessionBase session, int opCount, bool usedDiff, long startTimestamp);

    /// <summary>A session handed a frame of <paramref name="bytes" /> to its transport.</summary>
    void FrameSent(LiveSessionBase session, int bytes);

    /// <summary>A session received a well-formed inbound frame. The probe reads its <c>type</c> itself.</summary>
    void FrameReceived(LiveSessionBase session, int bytes, JsonElement frame);
}

/// <summary>The one place the runtime looks for an attached probe.</summary>
internal static class RaskDevToolsHook
{
    /// <summary>Set by <c>Rask.DevTools</c> when it activates; null otherwise.</summary>
    internal static IRaskDevToolsProbe? Probe { get; set; }

    /// <summary>
    ///     The probe to call, or null. Reads the feature switch first, so a build without the devtools folds
    ///     this to null and every call site's branch disappears with it.
    /// </summary>
    internal static IRaskDevToolsProbe? Active => RaskDevToolsFeature.IsEnabled ? Probe : null;
}
