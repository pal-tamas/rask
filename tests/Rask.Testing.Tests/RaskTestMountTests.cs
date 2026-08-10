#pragma warning disable RASK014 // test-defined components constructed directly

namespace Rask.Testing.Tests;

// #555: RaskTest.Render wraps the component under test in a forwarding root, and RenderAsLiveRootCore
// fires the lifecycle on the ROOT only — so the component itself was rendered but never mounted. OnMount
// and OnMountAsync never ran, which left anything that loads asynchronously stuck on its placeholder
// forever and pushed coverage that belongs in a unit test out to E2E. These pin the mount, the repaint
// that follows an asynchronous mount, and the guarantees that had to survive the fix.
public class RaskTestMountTests
{
    private sealed class Probe : Component
    {
        public List<string> Calls { get; } = [];

        protected override void OnMount() => Calls.Add(nameof(OnMount));

        protected override Task OnMountAsync()
        {
            Calls.Add(nameof(OnMountAsync));
            return Task.CompletedTask;
        }

        protected override void OnRendered(bool firstRender) => Calls.Add($"{nameof(OnRendered)}:{firstRender}");

        protected override void OnUnmount() => Calls.Add(nameof(OnUnmount));

        protected override Component Render() => Div["probe"];
    }

    [Fact]
    public void RenderComponent_MountsIt()
    {
        var probe = new Probe();
        RaskTest.Render(probe);

        Assert.Contains("OnMount", probe.Calls);
        Assert.Contains("OnMountAsync", probe.Calls);
    }

    [Fact]
    public void RenderComponent_MountsItExactlyOnce_AcrossReRenders()
    {
        var probe = new Probe();
        var page = RaskTest.Render(probe);
        page.Render();
        page.Render();

        Assert.Single(probe.Calls, c => c == "OnMount");
        Assert.Single(probe.Calls, c => c == "OnMountAsync");
    }

    [Fact]
    public void RenderComponent_ReachesTheAliveWalk_SoOnRenderedFires()
    {
        // Mounting is only half of it: adoption is what puts the component in the root's child map, which
        // is what CollectAlive walks. Without it the component is invisible to OnRendered and OnUnmount.
        var probe = new Probe();
        RaskTest.Render(probe);

        Assert.Contains("OnRendered:True", probe.Calls);
    }

    [Fact]
    public void RenderFactory_UnmountsAComponentItStopsReturning()
    {
        var probe = new Probe();
        var show = true;
        var page = RaskTest.Render(() => show ? probe : null);
        Assert.Contains("probe", page.Html);

        show = false;
        page.Render();

        Assert.Contains("OnUnmount", probe.Calls);
    }

    private sealed class SlowLoader : Component
    {
        private string? _loaded;

        protected override async Task OnMountAsync()
        {
            // ConfigureAwait(false) throughout, like the dashboard's PollingPanel: LifecycleSyncContext's
            // Post never fires, so the repaint can only come from the terminal StateHasChanged — which
            // needs the render handle this fix gives the component.
            await Task.Delay(20).ConfigureAwait(false);
            _loaded = "loaded";
        }

        protected override Component Render() => Div[_loaded ?? "placeholder"];
    }

    [Fact]
    public async Task WaitForAsync_SeesTheResultOfAnAsynchronousMount()
    {
        var page = RaskTest.Render(new SlowLoader());

        // The placeholder is what the old harness returned forever.
        Assert.Contains("placeholder", page.Html);

        var html = await page.WaitForAsync("loaded");

        Assert.Contains("loaded", html);
        Assert.DoesNotContain("placeholder", html);
    }

    private sealed class Stuck : Component
    {
        protected override Component Render() => Div["stuck"];
    }

    [Fact]
    public async Task WaitForAsync_ReportsTheLastMarkupWhenItGivesUp()
    {
        // A wait that fails should show what the component actually rendered — "it timed out" alone sends
        // you back to add the print statement the failure could have carried.
        var page = RaskTest.Render(new Stuck());

        var timeout = await Assert.ThrowsAsync<TimeoutException>(() =>
            page.WaitForAsync("never appears", TimeSpan.FromMilliseconds(50)));

        Assert.Contains("stuck", timeout.Message);
    }

    private sealed class SignalsAfterItsFirstRender : Component
    {
        private string _label = "before";

        protected override void OnRendered(bool firstRender)
        {
            if (!firstRender)
            {
                return;
            }

            // Note this is NOT OnMount: state set there needs no signal at all, because OnMount runs
            // before this component's own Render() in the same walk and is therefore already in the first
            // paint. This runs *after* it, from inside the walk's alive-set enumeration — so the new value
            // can only reach the markup if the request is queued and drained once the walk unwinds.
            // Answering it by rendering inline re-enters that enumeration and throws "Collection was
            // modified", which is what a live session avoids by coalescing rather than rendering on the
            // spot. Real components hit this without writing a StateHasChanged of their own: a callback
            // prop is wrapped by the generated factory in AutoCallback, which signals the owning parent
            // mid-walk.
            _label = "after";
            StateHasChanged();
        }

        protected override Component Render() => Div[_label];
    }

    [Fact]
    public void AStateChangeRaisedDuringTheWalk_IsQueuedAndDrainedRatherThanReentered()
    {
        var page = RaskTest.Render(new SignalsAfterItsFirstRender());

        // "after" is only in the markup if the queued render actually ran; reaching the assertion at all
        // is the other half — the inline answer threw out of the walk.
        Assert.Contains("after", page.Html);
        Assert.DoesNotContain("before", page.Html);
    }

    private sealed class Counter : Component
    {
        public int Mounts { get; private set; }

        protected override void OnMount() => Mounts++;

        protected override Component Render() => Div["counter"];
    }

    [Fact]
    public void Adoption_DoesNotPutTheInstanceUnderPositionalReuse()
    {
        // The guarantee RenderedComponent<T>.Instance documents. Adoption deliberately bypasses
        // GetOrCreateChild, whose reuse branch would make the instance subject to the positional cache.
        var counter = new Counter();
        var page = RaskTest.Render(counter);
        page.Render();
        page.Render();

        Assert.Same(counter, page.Instance);
        Assert.Equal(1, counter.Mounts);
    }

    [Fact]
    public void Adoption_DoesNotClearChildrenBuiltAtTheCallSite()
    {
        // The other half of that choice: GetOrCreateChild's reuse branch nulls Children, which would
        // delete a caller-built subtree on the second render.
        var page = RaskTest.Render(Div()[Span()["kept"]]);
        page.Render();
        page.Render();

        Assert.Contains("kept", page.Html);
    }
}
