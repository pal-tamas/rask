using Rask.Core;
using Rask.Core.Live;

namespace Rask.Islands;

/// <summary>
///     Gives an island's generated code a handler id for a delegate prop.
/// </summary>
/// <remarks>
///     <para>
///         A thin seam over <c>Component.RegisterHandler</c>, which is <c>internal</c> to Rask.Core.
///         The generated partial lives in the <em>app's</em> assembly, which has no access to Core's
///         internals — but this package does, so the seam lives here rather than widening Core's public
///         surface for one caller.
///     </para>
///     <para>
///         The id is the same one <c>data-rask-on-click</c> uses, and it carries the same guarantee:
///         stable per (component, slot) across re-renders. That stability is load-bearing for an
///         island. The client runtime keys its function cache on this id, so a callback handed to
///         React keeps its identity between updates — without it every render would hand React a
///         fresh function, invalidating any <c>useCallback</c> or <c>memo</c> that depends on it and
///         re-firing every <c>useEffect</c> that lists it.
///     </para>
/// </remarks>
public static class IslandHandlers
{
    /// <summary>Registers <paramref name="handler" /> against <paramref name="owner" />'s next slot.</summary>
    /// <param name="owner">The island the delegate was declared on.</param>
    /// <param name="handler">The delegate prop.</param>
    /// <returns>The handler id to write into the props JSON.</returns>
    public static string Register(Component owner, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(handler);

        // Tell the render context this page needs a live session. An island callback is exactly as
        // dependent on one as data-rask-on-click: the client's hostSend() needs __raskHost, which
        // only the Server and WASM runtimes publish. Without this the auto render ladder judges a
        // page whose only interactivity is an island callback to be static, serves it as a plain
        // document, and the first click reaches nobody — everything renders, the chunk loads, the
        // component mounts, and the UI is simply dead.
        //
        // Reported here rather than by routing through LiveRenderContext.RegisterHandler, which is
        // where every other handler is observed: that overload anchors the slot to CurrentParent —
        // the component whose subtree is being serialized — but an island's ids must anchor to the
        // ISLAND, or two islands under one parent would renumber each other's callbacks and break
        // the identity guarantee the client's function cache rests on.
        //
        // CurrentSync, not Current: props are written during the synchronous serialization walk, the
        // same path EmitDomEvent runs on. It reads null outside an active render, which is what
        // keeps a bare ToHtml() from claiming a page is interactive.
        LiveRenderContext.CurrentSync?.MarkRequiresLiveSession(InteractivityReason.Handler);

        return owner.RegisterHandler(handler);
    }
}
