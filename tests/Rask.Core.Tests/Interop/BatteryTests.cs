using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class BatteryTests
{
    [Fact]
    public async Task Asking_whether_the_battery_is_supported_calls_the_helper()
    {
        var js = new FakeJsRuntime();
        await new BrowserBattery(js).IsSupportedAsync();

        Assert.Equal("__raskBattery.isSupported", js.Calls.Single().Identifier);
    }

    [Fact]
    public async Task Getting_the_status_gives_the_reading()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBattery.getStatus", new BatteryStatus(0.42, true, 1800, null));

        var status = await new BrowserBattery(js).GetStatusAsync();

        Assert.Equal(new BatteryStatus(0.42, true, 1800, null), status);
    }

    [Fact]
    public async Task Getting_the_status_when_unsupported_gives_null() =>
        Assert.Null(await new BrowserBattery(new FakeJsRuntime()).GetStatusAsync());

    [Fact]
    public async Task Watching_registers_the_handler_and_starts_watching_under_an_id()
    {
        var js = new FakeJsRuntime();

        var watch = await new BrowserBattery(js).WatchAsync(_ => Task.CompletedTask);

        Assert.NotNull(watch);
        Assert.IsType<int>(js.ArgsFor("__raskBattery.watch")![0]);
    }

    [Fact]
    public async Task A_changed_reading_is_routed_to_the_registered_handler()
    {
        var js = new FakeJsRuntime();
        BatteryStatus? got = null;
        await new BrowserBattery(js).WatchAsync(s =>
        {
            got = s;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskBattery.watch")![0]!;

        await BatteryInterop.Changed(id, new BatteryStatus(0.9, false, null, 7200));

        Assert.Equal(new BatteryStatus(0.9, false, null, 7200), got);
    }

    [Fact]
    public async Task Disposing_clears_the_watch_and_stops_routing()
    {
        var js = new FakeJsRuntime();
        var received = 0;
        var watch = await new BrowserBattery(js).WatchAsync(_ =>
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
    public async Task A_change_for_an_unknown_id_does_nothing() =>
        await BatteryInterop.Changed(-999, new BatteryStatus(0.1, false, null, null));

    [Fact]
    public async Task Watching_with_a_null_arg_throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new BrowserBattery(new FakeJsRuntime()).WatchAsync(null!));
}
