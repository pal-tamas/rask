using Rask.Core;
using Rask.Core.Live;

namespace Rask.External;

/// <summary>
///     Gives an external component's generated code a handler id for a delegate prop.
/// </summary>
/// <remarks>
///     <para>
///         A thin seam over the render context's registration, which is <c>internal</c> to Rask.Core.
///         The generated partial lives in the <em>app's</em> assembly, which has no access to Core's
///         internals — but this package does, so the seam lives here rather than widening Core's
///         public surface for one caller.
///     </para>
///     <para>
///         The id is the same one <c>data-rask-on-click</c> uses, and it carries the same guarantee:
///         stable per (component, slot) across re-renders. That stability is load-bearing here. The
///         client runtime keys its function cache on this id, so a callback handed to React keeps its
///         identity between updates — without it every render would hand React a fresh function,
///         invalidating any <c>useCallback</c> or <c>memo</c> that depends on it and re-firing every
///         <c>useEffect</c> that lists it.
///     </para>
/// </remarks>
public static class ExternalHandlers
{
    /// <summary>Registers <paramref name="handler" /> against <paramref name="owner" />'s next slot.</summary>
    /// <param name="owner">The component the delegate was declared on.</param>
    /// <param name="handler">The delegate prop.</param>
    /// <returns>The handler id to write into the props JSON.</returns>
    /// <remarks>
    ///     <para>
    ///         Through the render context, NOT through <c>owner.RegisterHandler(handler)</c>. That
    ///         overload treats its receiver as the render root — the id source, the generation counter
    ///         and the handler map all live on one node — so calling it on the owner registers the
    ///         handler in the OWNER's map and restarts its id sequence at zero.
    ///     </para>
    ///     <para>
    ///         The consequences were both real and silent: the dispatcher only ever looks in the
    ///         root's map, so the callback resolved to nothing and never fired; and the restarted
    ///         sequence handed out an id the root had already given to an ordinary DOM handler, so a
    ///         page with a Rask button beside a component rendered <c>data-rask-on-click="h0"</c> and
    ///         <c>"onBump":{"$h":"h0"}</c> — the same id for two different handlers.
    ///     </para>
    ///     <para>
    ///         Registering through the context also reports the page as interactive, which is what
    ///         stops the auto render ladder serving it as a static document with no session to send on.
    ///     </para>
    /// </remarks>
    public static string Register(Component owner, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(handler);

        // CurrentSync, not Current: props are written during the synchronous serialization walk, the
        // same path the DOM event emitter runs on. It reads null outside an active render, which is
        // what keeps a bare ToHtml() from claiming a page is interactive.
        var context = LiveRenderContext.CurrentSync;
        if (context is null)
        {
            // No live render — a bare ToHtml() in a test or a static prerender pass. There is no root
            // to register against and nothing will dispatch, so number it against the owner alone
            // rather than pretending otherwise.
            return owner.RegisterHandler(handler);
        }

        return context.RegisterHandlerFor(owner, handler);
    }
}
