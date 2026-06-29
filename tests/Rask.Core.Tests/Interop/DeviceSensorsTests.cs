using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class DeviceSensorsTests
{
    [Theory]
    [InlineData("granted", SensorPermission.Granted)]
    [InlineData("denied", SensorPermission.Denied)]
    [InlineData(null, SensorPermission.Denied)]
    public async Task Orientation_RequestPermission_MapsResult(string? raw, SensorPermission expected)
    {
        var js = new FakeJsRuntime();
        if (raw is not null)
        {
            js.SetResponse("__raskDeviceOrientation.requestPermission", raw);
        }

        Assert.Equal(expected, await new DeviceOrientation(js).RequestPermissionAsync());
    }

    [Fact]
    public async Task Orientation_Watch_RoutesReading_AndDisposeStops()
    {
        var js = new FakeJsRuntime();
        OrientationReading? got = null;
        var watch = await new DeviceOrientation(js).WatchAsync(r =>
        {
            got = r;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskDeviceOrientation.watch")![0]!;

        await DeviceOrientationInterop.Reading(id, new OrientationReading(10, 20, 30, true));
        Assert.NotNull(got);
        Assert.Equal(20, got!.Beta);

        await watch.DisposeAsync();
        got = null;
        await DeviceOrientationInterop.Reading(id, new OrientationReading(1, 1, 1, false)); // unregistered → no-op

        Assert.Equal([id], js.ArgsFor("__raskDeviceOrientation.clear"));
        Assert.Null(got);
    }

    [Fact]
    public async Task Motion_Watch_RoutesReading_AndDisposeStops()
    {
        var js = new FakeJsRuntime();
        MotionReading? got = null;
        var watch = await new DeviceMotion(js).WatchAsync(r =>
        {
            got = r;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskDeviceMotion.watch")![0]!;

        await DeviceMotionInterop.Reading(id, new MotionReading(1, 2, 3, 4, 5, 6, 16));
        Assert.NotNull(got);
        Assert.Equal(3, got!.AccelerationZ);
        Assert.Equal(16, got.Interval);

        await watch.DisposeAsync();
        Assert.Equal([id], js.ArgsFor("__raskDeviceMotion.clear"));
    }

    [Fact]
    public async Task Watch_NullHandler_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new DeviceOrientation(new FakeJsRuntime()).WatchAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new DeviceMotion(new FakeJsRuntime()).WatchAsync(null!));
    }
}
