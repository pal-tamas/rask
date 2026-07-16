using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class BatteryTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        await new Battery(js).IsSupportedAsync();
        Assert.Equal("__raskBattery.isSupported", js.Calls.Single().Identifier);
    }

    [Fact]
    public async Task GetStatus_ReturnsTheReading()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBattery.getStatus", new BatteryStatus(0.42, true, 1800, null));

        var status = await new Battery(js).GetStatusAsync();

        Assert.Equal(new BatteryStatus(0.42, true, 1800, null), status);
    }

    [Fact]
    public async Task GetStatus_Unsupported_IsNull() =>
        Assert.Null(await new Battery(new FakeJsRuntime()).GetStatusAsync());

    [Fact]
    public async Task Watch_RegistersHandler_AndStartsWatchingUnderAnId()
    {
        var js = new FakeJsRuntime();

        var watch = await new Battery(js).WatchAsync(_ => Task.CompletedTask);

        Assert.NotNull(watch);
        Assert.IsType<int>(js.ArgsFor("__raskBattery.watch")![0]);
    }

    [Fact]
    public async Task Changed_RoutesReading_ToTheRegisteredHandler()
    {
        var js = new FakeJsRuntime();
        BatteryStatus? got = null;
        await new Battery(js).WatchAsync(s =>
        {
            got = s;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskBattery.watch")![0]!;

        await BatteryInterop.Changed(id, new BatteryStatus(0.9, false, null, 7200));

        Assert.Equal(new BatteryStatus(0.9, false, null, 7200), got);
    }

    [Fact]
    public async Task Dispose_ClearsWatch_AndStopsRouting()
    {
        var js = new FakeJsRuntime();
        var received = 0;
        var watch = await new Battery(js).WatchAsync(_ =>
        {
            received++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskBattery.watch")![0]!;

        await watch.DisposeAsync();
        await BatteryInterop.Changed(id, new BatteryStatus(0.5, true, null, null)); // unregistered → no-op

        Assert.Equal([id], js.ArgsFor("__raskBattery.clear"));
        Assert.Equal(0, received);
    }

    [Fact]
    public async Task Changed_UnknownId_IsNoOp() =>
        await BatteryInterop.Changed(-999, new BatteryStatus(0.1, false, null, null));

    [Fact]
    public async Task Watch_NullArg_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new Battery(new FakeJsRuntime()).WatchAsync(null!));
}
