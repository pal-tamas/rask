using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class GamepadTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskGamepad.isSupported", true);

        Assert.True(await new Gamepad(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Watch_RegistersHandler_AndStartsPolling()
    {
        var js = new FakeJsRuntime();

        await new Gamepad(js).WatchAsync(_ => Task.CompletedTask);

        var args = js.ArgsFor("__raskGamepad.watch");
        Assert.IsType<int>(args![0]);
    }

    [Fact]
    public async Task Reading_RoutesToRegisteredHandler()
    {
        var js = new FakeJsRuntime();
        GamepadReading? got = null;
        await new Gamepad(js).WatchAsync(r =>
        {
            got = r;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskGamepad.watch")![0]!;

        var reading = new GamepadReading(0, "Mock Pad", true, [0.5, -0.5], [1.0, 0.0]);
        await GamepadInterop.Reading(id, reading);

        Assert.Same(reading, got);
    }

    [Fact]
    public async Task Dispose_Unwatches_AndStopsRouting()
    {
        var js = new FakeJsRuntime();
        var fired = 0;
        var handle = await new Gamepad(js).WatchAsync(_ =>
        {
            fired++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskGamepad.watch")![0]!;

        await handle.DisposeAsync();
        Assert.Equal([id], js.ArgsFor("__raskGamepad.unwatch"));

        await GamepadInterop.Reading(id, new GamepadReading(0, "x", false, [], []));
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Watch_NullHandler_Throws()
    {
        var svc = new Gamepad(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.WatchAsync(null!));
    }
}
