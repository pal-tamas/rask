using System.Text.Json;
using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class GeolocationTests
{
    [Fact]
    public async Task GetCurrentPosition_Default_SendsHelper_WithDefaultOptions()
    {
        var js = new FakeJsRuntime();
        var geo = new Geolocation(js);

        await geo.GetCurrentPositionAsync();

        // (enableHighAccuracy, timeoutMs, maximumAgeMs) — defaults: false, null (=> Infinity in JS), 0.
        Assert.Equal([false, null, 0], js.ArgsFor("__raskApi.geolocation"));
    }

    [Fact]
    public async Task GetCurrentPosition_PassesOptionsThrough()
    {
        var js = new FakeJsRuntime();
        var geo = new Geolocation(js);

        await geo.GetCurrentPositionAsync(new GeolocationOptions
        {
            EnableHighAccuracy = true,
            TimeoutMs = 5000,
            MaximumAgeMs = 1000
        });

        Assert.Equal([true, 5000, 1000], js.ArgsFor("__raskApi.geolocation"));
    }

    [Fact]
    public async Task GetCurrentPosition_ReturnsCannedPosition()
    {
        var js = new FakeJsRuntime();
        var expected = new GeolocationPosition(51.5, -0.12, 12.0, null, null, null, null, 1_700_000_000_000);
        js.SetResponse("__raskApi.geolocation", expected);
        var geo = new Geolocation(js);

        var pos = await geo.GetCurrentPositionAsync();

        Assert.Equal(expected, pos);
    }

    [Fact]
    public void Position_DeserializesFrom_HelperCamelCaseJson()
    {
        // The contract between __raskApi.geolocation (rask-api.js) and the C# record: the helper
        // emits camelCase coords; GeolocationPosition must map them under JSInterop's Web defaults.
        const string json = """
            {
              "latitude": 51.5,
              "longitude": -0.12,
              "accuracy": 12.5,
              "altitude": 30.0,
              "altitudeAccuracy": 4.0,
              "heading": 90.0,
              "speed": 1.5,
              "timestampMs": 1700000000000
            }
            """;

        var pos = JsonSerializer.Deserialize<GeolocationPosition>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(pos);
        Assert.Equal(51.5, pos!.Latitude);
        Assert.Equal(-0.12, pos.Longitude);
        Assert.Equal(12.5, pos.Accuracy);
        Assert.Equal(30.0, pos.Altitude);
        Assert.Equal(4.0, pos.AltitudeAccuracy);
        Assert.Equal(90.0, pos.Heading);
        Assert.Equal(1.5, pos.Speed);
        Assert.Equal(1_700_000_000_000, pos.TimestampMs);
    }

    [Fact]
    public void Position_NullableFields_DeserializeToNull_WhenAbsent()
    {
        const string json = """
            {"latitude":1.0,"longitude":2.0,"accuracy":3.0,"altitude":null,"altitudeAccuracy":null,"heading":null,"speed":null,"timestampMs":0}
            """;

        var pos = JsonSerializer.Deserialize<GeolocationPosition>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(pos);
        Assert.Null(pos!.Altitude);
        Assert.Null(pos.Speed);
    }
}
