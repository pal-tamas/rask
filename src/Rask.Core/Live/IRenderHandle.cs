namespace Rask.Core.Live;

public interface IRenderHandle
{
    Task RequestRenderAsync();

    /// <summary>
    ///     Request a "publish" render — same render walk, but skips re-firing
    ///     <c>OnRendered</c> / <c>OnRenderedAsync</c> on components that have
    ///     already rendered at least once. The framework uses this from
    ///     <c>OnRenderedAsync</c>'s continuation so state mutated post-await
    ///     paints, without re-entering the very lifecycle hook that scheduled the
    ///     render (which would loop). First-time renders still fire their hooks so
    ///     newly-mounted components get their first <c>OnRendered(firstRender:true)</c>.
    ///     Hosts that don't need the distinction can let the default impl forward to
    ///     <see cref="RequestRenderAsync" />.
    /// </summary>
    Task RequestPublishRenderAsync() => RequestRenderAsync();

    internal Task RenderInScopeAsync() => Task.CompletedTask;

    /// <summary>
    ///     Records a development fault to paint <em>over</em> the app, reported by
    ///     <c>RootErrorBoundary</c> during the render walk that follows it.
    /// </summary>
    /// <remarks>
    ///     It goes to the handle rather than staying on the <see cref="LiveRenderContext" /> because the
    ///     context is disposed when the walk ends, and the frame is built afterwards — a session reading
    ///     it from the context would find nothing. The session holds it until it writes the payload.
    ///     <para>
    ///         Defaulted to a no-op so the interface stays non-breaking, and so the non-session
    ///         implementations (the unit-test render handle) simply don't have an overlay.
    ///     </para>
    /// </remarks>
    internal void ReportDevError(DevErrorInfo error)
    {
    }

    // Host-awareness axes surfaced to components during render (via LiveRenderContext → Component.HostShell/…).
    // Defaulted here so the interface stays non-breaking; concrete sessions override with their own facts.
    internal RenderShell Shell => RenderShell.Web;
    internal RenderEngine Engine => RenderEngine.Server;
    internal RenderPlatform Platform => RenderPlatform.None;

    // Native-chrome collection: only the native host (with an INativeChrome backend registered) opts in, so the
    // serializer hands each user component it walks to the session, which picks out the native bars composed in
    // the tree (Rask.Core stays free of any Rask.Native type — the component arrives as a plain Component and the
    // native session classifies it). Reporting mid-walk keeps the bars' factories DI-correct and callback-owner
    // wired. Last bar of a kind in the pre-order walk wins. No-op everywhere else.
    internal bool CollectsNativeChrome => false;
    internal void ReportNativeComponent(Component component) { }
}
