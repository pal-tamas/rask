using Rask.Core;
using Rask.Core.Live;

namespace Rask.Testing;

// Forwarding live-render root. The component under test is rendered as this root's only child, so it
// reconciles through the normal live-render path (the same shape Rask.TestSupport's StubComponent uses)
// and adds no markup of its own. The factory re-runs per render, so a test's tree reflects its current
// state; Render(Component) passes a factory that returns the one instance every time.
internal sealed partial class TestRoot(Func<Component?> factory) : Component
{
    protected override Component? Render()
    {
        var child = factory();
        if (child is null || LiveRenderContext.Current is null)
        {
            return child;
        }

        // Adopt and mount the child, exactly as the framework's own two wrapper roots do for theirs
        // (RootErrorBoundary for the App, RouteChainPages for a page). RenderAsLiveRootCore fires
        // the lifecycle on the ROOT only — which here is this forwarding wrapper, not the component
        // under test — so a component handed to Render() as an object rendered forever without
        // OnMount ever running, leaving anything that loads asynchronously stuck on
        // its placeholder. A child the factory built through its generated factory has already been
        // adopted and notified by GetOrCreate inside this render; both calls below are no-ops for it.
        AdoptChild(child, RenderHandle);
        LiveRenderContext.NotifyParameters(child, propsChanged: false);
        return child;
    }
}
