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
    ///     <para>
    ///         Turning it off is a declaration rather than a preference: <b>no page ever gets a live
    ///         session</b>. Every page is served as a document — no session, no socket, no runtime
    ///         script — and the WebSocket endpoint answers 404 as though it were not there.
    ///     </para>
    ///     <para>
    ///         That is stronger than <see cref="Static" />, which is <em>detected</em> per page and
    ///         deliberately biased towards keeping a connection. Here nothing is detected, so nothing
    ///         can bias: an app that serves only content gets only content.
    ///     </para>
    ///     <para>
    ///         With <see cref="Wasm" /> on it is the offline-first, edge-hosted arrangement — static
    ///         HTML that hands over to WebAssembly, with no socket ever opened. With <see cref="Wasm" />
    ///         off it is plain server-side rendering, which is the right answer for a content site and
    ///         used to be refused.
    ///     </para>
    ///     <para>
    ///         The cost is real and is reported rather than prevented: a page that renders a handler
    ///         has nothing to answer it, and says so through the <c>Rask.Ssr</c> diagnostic. Refusing
    ///         to start was the wrong response to that — it made "serve only content" unreachable.
    ///     </para>
    /// </remarks>
    public bool ServerInteractivity { get; set; } = true;

    /// <summary>
    ///     Let an eligible page move into the browser once the WebAssembly bundle is available.
    ///     Default <c>false</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The bundle is fetched once the page is idle, never on the critical path, and the page
    ///         is fully usable over its WebSocket the whole time. When it is ready the next navigation
    ///         renders in the browser and the socket closes. A bundle that fails to load changes
    ///         nothing: the page stays server-live, which is what it already was.
    ///     </para>
    ///     <para>
    ///         The bundle must be published with <c>WasmFingerprintAssets=false</c>. The WebAssembly
    ///         SDK otherwise content-hashes the framework files and maps them through an import map it
    ///         writes into the bundle's own <c>index.html</c> — a document the visitor never loads
    ///         here, since the page comes from the server. Turned off, the framework files sit at
    ///         their literal paths and need no map. Cache-busting is the server's to do, which it is
    ///         already equipped for in a way a static host is not.
    ///     </para>
    /// </remarks>
    public bool Wasm { get; set; }

    /// <summary>
    ///     Where the browser bundle's boot module is served, root-relative. Default <c>/main.js</c>,
    ///     which is where a Rask WASM bundle puts it.
    /// </summary>
    /// <remarks>
    ///     Resolved against the app's path base, like every other framework asset URL. Only read when
    ///     <see cref="Wasm" /> is on.
    /// </remarks>
    public string WasmBundle { get; set; } = "/main.js";

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

        if (Wasm && string.IsNullOrWhiteSpace(WasmBundle))
        {
            throw new InvalidOperationException(
                "RenderModes.Wasm is on but RenderModes.WasmBundle is empty, so no page would know "
                + "where to fetch the browser bundle and none would ever move into it. Set it to the "
                + "boot module's URL, or leave it at its default of /main.js.");
        }

    }
}
