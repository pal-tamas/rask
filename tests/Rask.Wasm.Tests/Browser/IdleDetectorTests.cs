using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class IdleDetectorTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdle.isSupported", true);

        Assert.True(await new IdleDetectorService(js).IsSupportedAsync());
    }

    [Fact]
    public async Task RequestPermission_ReturnsState()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskIdle.requestPermission", "granted");

        Assert.Equal("granted", await new IdleDetectorService(js).RequestPermissionAsync());
    }

    [Fact]
    public async Task Watch_RegistersHandler_AndPassesThreshold()
    {
        var js = new FakeJsRuntime();

        await new IdleDetectorService(js).WatchAsync(_ => Task.CompletedTask, 120);

        var args = js.ArgsFor("__raskIdle.watch");
        Assert.IsType<int>(args![0]);
        Assert.Equal(120, args[1]);
    }

    [Fact]
    public async Task Changed_RoutesToRegisteredHandler()
    {
        var js = new FakeJsRuntime();
        IdleReading? got = null;
        await new IdleDetectorService(js).WatchAsync(r =>
        {
            got = r;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskIdle.watch")![0]!;

        var reading = new IdleReading(true, false);
        await IdleDetectorInterop.Changed(id, reading);

        Assert.Same(reading, got);
    }

    [Fact]
    public async Task Dispose_Unwatches_AndStopsRouting()
    {
        var js = new FakeJsRuntime();
        var fired = 0;
        var handle = await new IdleDetectorService(js).WatchAsync(_ =>
        {
            fired++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskIdle.watch")![0]!;

        await handle.DisposeAsync();
        Assert.Equal([id], js.ArgsFor("__raskIdle.unwatch"));

        await IdleDetectorInterop.Changed(id, new IdleReading(false, false));
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Watch_NullHandler_Throws()
    {
        var svc = new IdleDetectorService(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.WatchAsync(null!));
    }
}
