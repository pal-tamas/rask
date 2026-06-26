using Rask.Core;
using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class IntersectionObserverTests
{
    [Fact]
    public async Task Observe_PassesElementAndOptions()
    {
        var js = new FakeJsRuntime();
        var el = ElementRef.New();
        var opts = new IntersectionOptions { Thresholds = [0, 0.5, 1], RootMargin = "100px" };

        await new IntersectionObserverService(js).ObserveAsync(el, _ => Task.CompletedTask, opts);

        var args = js.ArgsFor("__raskIntersect.observe");
        Assert.IsType<int>(args![0]);            // id
        Assert.Same(el, args[1]);                // element ref
        Assert.Equal(new double[] { 0, 0.5, 1 }, args[2]);
        Assert.Equal("100px", args[3]);
    }

    [Fact]
    public async Task Changed_RoutesEntry_ToTheRegisteredHandler()
    {
        var js = new FakeJsRuntime();
        IntersectionEntry? got = null;
        await new IntersectionObserverService(js).ObserveAsync(ElementRef.New(), e =>
        {
            got = e;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskIntersect.observe")![0]!;

        await IntersectionInterop.Changed(id, new IntersectionEntry(true, 0.75));

        Assert.NotNull(got);
        Assert.True(got!.IsIntersecting);
        Assert.Equal(0.75, got.Ratio);
    }

    [Fact]
    public async Task Dispose_StopsObserving_AndRouting()
    {
        var js = new FakeJsRuntime();
        var hits = 0;
        var obs = await new IntersectionObserverService(js).ObserveAsync(ElementRef.New(), _ =>
        {
            hits++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskIntersect.observe")![0]!;

        await obs.DisposeAsync();
        await IntersectionInterop.Changed(id, new IntersectionEntry(true, 1)); // unregistered → no-op

        Assert.Equal([id], js.ArgsFor("__raskIntersect.unobserve"));
        Assert.Equal(0, hits);
    }

    [Fact]
    public async Task Observe_NullArgs_Throw()
    {
        var svc = new IntersectionObserverService(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await svc.ObserveAsync(null!, _ => Task.CompletedTask));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await svc.ObserveAsync(ElementRef.New(), null!));
    }
}
