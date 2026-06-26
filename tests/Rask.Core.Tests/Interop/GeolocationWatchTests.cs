using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class GeolocationWatchTests
{
    private static GeolocationPosition Pos(double lat, double lng) =>
        new(lat, lng, 10, null, null, null, null, 0);

    [Fact]
    public async Task Watch_PassesOptions_ToHelper()
    {
        var js = new FakeJsRuntime();
        var opts = new GeolocationOptions { EnableHighAccuracy = true, TimeoutMs = 5000, MaximumAgeMs = 1000 };

        await new Geolocation(js).WatchAsync(_ => Task.CompletedTask, opts);

        var args = js.ArgsFor("__raskGeoWatch.watch");
        Assert.IsType<int>(args![0]);     // id
        Assert.Equal(true, args[1]);      // enableHighAccuracy
        Assert.Equal(5000, args[2]);      // timeoutMs
        Assert.Equal(1000, args[3]);      // maximumAgeMs
    }

    [Fact]
    public async Task Fix_RoutesPosition_ToTheRegisteredHandler()
    {
        var js = new FakeJsRuntime();
        GeolocationPosition? got = null;
        await new Geolocation(js).WatchAsync(p =>
        {
            got = p;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskGeoWatch.watch")![0]!;

        await GeolocationWatchInterop.Fix(id, Pos(51.5, -0.12));

        Assert.NotNull(got);
        Assert.Equal(51.5, got!.Latitude);
        Assert.Equal(-0.12, got.Longitude);
    }

    [Fact]
    public async Task Dispose_ClearsWatch_AndStopsRouting()
    {
        var js = new FakeJsRuntime();
        var hits = 0;
        var watch = await new Geolocation(js).WatchAsync(_ =>
        {
            hits++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskGeoWatch.watch")![0]!;

        await watch.DisposeAsync();
        await GeolocationWatchInterop.Fix(id, Pos(1, 1)); // unregistered → no-op

        Assert.Equal([id], js.ArgsFor("__raskGeoWatch.clear"));
        Assert.Equal(0, hits);
    }

    [Fact]
    public async Task Watch_NullHandler_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new Geolocation(new FakeJsRuntime()).WatchAsync(null!));
    }
}
