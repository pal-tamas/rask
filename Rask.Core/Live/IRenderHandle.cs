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
}
