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
