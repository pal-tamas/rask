using Rask.Core.Routing;
using Rask.Example.Shared;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Layout;

public sealed partial class PathDisplayTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_DisplaysCurrentRoutePath()
    {
        var routeState = new RouteState { Path = "/abc" };
        var html = new PathDisplay(routeState).RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("/abc", html);
        Assert.Contains("text-info", html);
    }

    [Fact]
    public void OnMount_SubscribesToRouteChanged_OnUnmount_Unsubscribes()
    {
        var routeState = new RouteState();
        // Use the generated factory so the framework registers PathDisplay as a child
        // and propagates the parent's RenderHandle — needed for StateHasChanged to
        // actually queue a re-render. `new PathDisplay(...)` would bypass that.
        var host = new LiveHost(
            () => PathDisplay,
            TestServices.Default(routeState: routeState));

        host.RenderAsLiveRoot();
        var rendersBeforeMutation = host.Handle.RequestRenderCount;
        routeState.Path = "/changed-once";
        var rendersAfterMutation = host.Handle.RequestRenderCount;
        Assert.True(rendersAfterMutation > rendersBeforeMutation,
            "expected PathDisplay to request a re-render when RouteState.Path changes after mount");

        // Tear the component out of the tree, then mutate route again. If the unmount
        // didn't unsubscribe, the now-orphan StateHasChanged would still queue a render.
        host.Mounted = false;
        host.RenderAsLiveRoot();
        var rendersAfterUnmount = host.Handle.RequestRenderCount;
        routeState.Path = "/changed-after-unmount";
        Assert.Equal(rendersAfterUnmount, host.Handle.RequestRenderCount);
    }
}
