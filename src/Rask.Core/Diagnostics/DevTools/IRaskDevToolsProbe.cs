using System.Text.Json;
using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Core.Diagnostics.DevTools;

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
    ///     A component and its whole subtree were serialized. <paramref name="parent" /> is the component it was
    ///     walked inside — its place on the page, not whoever constructed it — or null outside a live render.
    ///     <paramref name="frameStart" /> and <paramref name="frameEnd" /> bracket what it wrote into the live frame
    ///     stream, or are -1 when the walk captured no frames.
    /// </summary>
    void ComponentWalked(Component component, Component? parent, long startTimestamp, int frameStart, int frameEnd);

    /// <summary>A clean component was replayed from its cached frames instead of being walked.</summary>
    void ComponentReplayed(Component component, Component? parent, int frameStart, int frameEnd);

    /// <summary>
    ///     A component's render or walk threw. Used as an exception filter, so it MUST return false: the
    ///     exception keeps propagating to the boundary that handles it, and the stack is not unwound here.
    /// </summary>
    bool ObserveThrow(Component component, Exception exception);

    /// <summary>
    ///     A component's handler or async lifecycle hook threw. <paramref name="caught" /> says whether an error boundary
    ///     took the exception; when none did, the runtime reports it through <see cref="RaskDiagnostics" /> too. A render
    ///     that throws is seen through <see cref="ObserveThrow" /> instead, while it is still unwinding.
    /// </summary>
    void ComponentFaulted(Component component, Exception exception, ErrorSource source, bool caught);

    /// <summary>
    ///     The framework reported a diagnostic, whether or not a sink is listening. Called on the reporting thread, before
    ///     the sink.
    /// </summary>
    void DiagnosticReported(in RaskDiagnosticEvent diagnostic);

    /// <summary>
    ///     A <see cref="Context" /> provider pushed its value for the subtree walked inside it. <paramref name="owner" /> is
    ///     the component whose markup holds the provider. Called right after the push, so the stack's head is its entry.
    /// </summary>
    void ContextProvided(Context provider, Component owner);

    /// <summary>
    ///     A component read a context value while rendering — through <c>Get</c>, <c>Required</c> or <c>Has</c> — before the
    ///     read resolves. Not called for a read outside a live render.
    /// </summary>
    void ContextRead(Component reader, Type requested, string? name);

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
