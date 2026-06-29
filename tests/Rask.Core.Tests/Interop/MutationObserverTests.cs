using Rask.Core;
using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class MutationObserverTests
{
    [Fact]
    public async Task Observe_PassesElementAndOptions()
    {
        var js = new FakeJsRuntime();
        var el = ElementRef.New();
        var opts = new MutationOptions
        {
            ChildList = true,
            Attributes = true,
            CharacterData = true,
            Subtree = true,
            AttributeFilter = ["class", "data-x"]
        };

        await new MutationObserverService(js).ObserveAsync(el, _ => Task.CompletedTask, opts);

        var args = js.ArgsFor("__raskMutation.observe");
        Assert.IsType<int>(args![0]);            // id
        Assert.Same(el, args[1]);                // element ref
        Assert.Equal(true, args[2]);             // childList
        Assert.Equal(true, args[3]);             // attributes
        Assert.Equal(true, args[4]);             // characterData
        Assert.Equal(true, args[5]);             // subtree
        Assert.Equal(new[] { "class", "data-x" }, args[6]);
    }

    [Fact]
    public async Task Observe_DefaultOptions_WatchChildList()
    {
        var js = new FakeJsRuntime();

        await new MutationObserverService(js).ObserveAsync(ElementRef.New(), _ => Task.CompletedTask);

        var args = js.ArgsFor("__raskMutation.observe");
        Assert.Equal(true, args![2]);            // childList defaults true
        Assert.Equal(false, args[3]);            // attributes defaults false
    }

    [Fact]
    public async Task Changed_RoutesEntry_ToTheRegisteredHandler()
    {
        var js = new FakeJsRuntime();
        MutationEntry? got = null;
        await new MutationObserverService(js).ObserveAsync(ElementRef.New(), e =>
        {
            got = e;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskMutation.observe")![0]!;

        await MutationInterop.Changed(id, new MutationEntry("attributes", 0, 0, "class"));

        Assert.NotNull(got);
        Assert.Equal("attributes", got!.Type);
        Assert.Equal("class", got.AttributeName);
    }

    [Fact]
    public async Task Dispose_StopsObserving_AndRouting()
    {
        var js = new FakeJsRuntime();
        var hits = 0;
        var obs = await new MutationObserverService(js).ObserveAsync(ElementRef.New(), _ =>
        {
            hits++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskMutation.observe")![0]!;

        await obs.DisposeAsync();
        await MutationInterop.Changed(id, new MutationEntry("childList", 1, 0, null)); // unregistered → no-op

        Assert.Equal([id], js.ArgsFor("__raskMutation.unobserve"));
        Assert.Equal(0, hits);
    }

    [Fact]
    public async Task Observe_NullArgs_Throw()
    {
        var svc = new MutationObserverService(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await svc.ObserveAsync(null!, _ => Task.CompletedTask));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await svc.ObserveAsync(ElementRef.New(), null!));
    }
}
