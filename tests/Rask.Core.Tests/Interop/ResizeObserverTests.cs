using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class ResizeObserverTests
{
    [Fact]
    public async Task Observing_passes_the_element()
    {
        var js = new FakeJsRuntime();
        var el = ElementRef.New();

        await new ResizeObserverService(js).ObserveAsync(el, _ => Task.CompletedTask);

        var args = js.ArgsFor("__raskResize.observe");
        Assert.IsType<int>(args![0]);
        Assert.Same(el, args[1]);
    }

    [Fact]
    public async Task A_changed_entry_is_routed_to_the_registered_handler()
    {
        var js = new FakeJsRuntime();
        ResizeEntry? got = null;
        await new ResizeObserverService(js).ObserveAsync(ElementRef.New(), e =>
        {
            got = e;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskResize.observe")![0]!;

        await ResizeInterop.Changed(id, new ResizeEntry(320, 240));

        Assert.NotNull(got);
        Assert.Equal(320, got!.Width);
        Assert.Equal(240, got.Height);
    }

    [Fact]
    public async Task Disposing_stops_observing_and_routing()
    {
        var js = new FakeJsRuntime();
        var hits = 0;
        var obs = await new ResizeObserverService(js).ObserveAsync(ElementRef.New(), _ =>
        {
            hits++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskResize.observe")![0]!;

        await obs.DisposeAsync();
        await ResizeInterop.Changed(id, new ResizeEntry(10, 10)); // unregistered → no-op

        Assert.Equal([id], js.ArgsFor("__raskResize.unobserve"));
        Assert.Equal(0, hits);
    }

    [Fact]
    public async Task Observing_with_null_args_throws()
    {
        var svc = new ResizeObserverService(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await svc.ObserveAsync(null!, _ => Task.CompletedTask));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await svc.ObserveAsync(ElementRef.New(), null!));
    }
}
