namespace Rask.Server;

/// <summary>
///     Which rungs of the render ladder this app uses.
/// </summary>
/// <remarks>
///     <para>
///         A Rask page climbs as far as it needs to and no further: it is served as a document, it
///         streams if its data is slow, it becomes live over a WebSocket if something on it needs a
///         connection, and it moves into the browser once the bundle is there. Each rung is
///         automatic — nothing here has to be set for a page to work.
///     </para>
///     <para>
///         These switches are the ceiling, for an app that wants a rung it will never use turned off
///         rather than merely unused: an edge-hosted app that must never open a WebSocket, a content
///         site that wants HTML and nothing else. A page can opt DOWN from this ceiling with its own
///         attribute; it cannot opt above it.
///     </para>
///     <para>
///         A combination that cannot work throws when the host is built, naming what is off. A
///         contradiction here is a configuration mistake, and a host that refuses to start is far
///         cheaper to diagnose than a page that silently does nothing in production.
///     </para>
/// </remarks>
public sealed class RaskRenderModes
{
    /// <summary>
    ///     Serve a page that needs nothing live as a plain document — no session, no WebSocket, no
    ///     runtime script — and let it be cached. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    ///     Off by default because whether a page needs a connection is detected from what its render
    ///     did, and a component that pushes from a timer or an <c>event</c> subscription shows the
    ///     walk nothing to detect. Such a page reports itself the first time it tries to update (see
    ///     the <c>Rask.Ssr</c> warning), but it reports it in production. Check the pages this
    ///     changes before turning it on.
    /// </remarks>
    public bool Static { get; set; }

    /// <summary>
    ///     Flush the shell before a page's slow data has arrived and stream the rest. Default
    ///     <c>false</c>.
    /// </summary>
    /// <remarks>
    ///     Not yet implemented; setting it throws at startup rather than silently doing nothing.
    ///     Until it lands, <see cref="QuiescenceTimeout" /> is what bounds a slow page.
    /// </remarks>
    public bool Streaming { get; set; }

    /// <summary>
    ///     Let a page become interactive over a WebSocket. Default <c>true</c> — this is how Rask has
    ///     always worked.
    /// </summary>
    /// <remarks>
    ///     Turning it off means a page is served as HTML and becomes interactive only once the
    ///     browser bundle boots: no socket is ever opened. That is the offline-first and edge-hosted
    ///     arrangement, and it requires <see cref="Wasm" />, since otherwise a page with a handler
    ///     would have no way to answer it at all.
    /// </remarks>
    public bool ServerInteractivity { get; set; } = true;

    /// <summary>
    ///     Let an eligible page move into the browser once the WebAssembly bundle is available.
    ///     Default <c>false</c>.
    /// </summary>
    /// <remarks>Not yet implemented; setting it throws at startup rather than silently doing nothing.</remarks>
    public bool Wasm { get; set; }

    /// <summary>
    ///     How long the initial <c>GET</c> waits for a page's async lifecycle work to settle before
    ///     serving its HTML. <see cref="TimeSpan.Zero" /> disables the wait. Default 5&#160;seconds.
    /// </summary>
    /// <remarks>
    ///     Without the wait, a page that loads its data in <c>OnMountAsync</c> serves its
    ///     placeholder as the first paint and as the whole document a crawler sees. Blowing the
    ///     budget is not an error: the page is served as it stands and keeps a live session, so it
    ///     finishes loading over the socket. A slow page does hold a request open for up to this
    ///     long, so size it together with the session cap — the two multiply.
    /// </remarks>
    public TimeSpan QuiescenceTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Refuses a combination that cannot serve a working page. Called when the host is built.
    /// </summary>
    internal void Validate()
    {
        // Announced rather than ignored. A switch that reads as supported and does nothing is worse
        // than one that is absent: the app looks configured for something it is not doing.
        if (Streaming)
        {
            throw new InvalidOperationException(
                "RenderModes.Streaming is not implemented yet, so turning it on would change nothing. "
                + "A page with slow data is bounded by RenderModes.QuiescenceTimeout until it lands.");
        }

        if (Wasm)
        {
            throw new InvalidOperationException(
                "RenderModes.Wasm is not implemented yet, so turning it on would change nothing. "
                + "Pages become interactive over a WebSocket for now.");
        }

        if (!ServerInteractivity && !Wasm)
        {
            throw new InvalidOperationException(
                "RenderModes.ServerInteractivity is off and RenderModes.Wasm is off, which leaves no "
                + "way for any page to become interactive: a click would reach neither a server "
                + "session nor a browser runtime. Turn one of them on. If this app genuinely serves "
                + "only content, leave ServerInteractivity on — a page that needs nothing live is "
                + "already served as a plain document when RenderModes.Static is enabled.");
        }
    }
}
