using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class NetworkInfoTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.networkSupported", true);

        Assert.True(await new NetworkInfo(js).IsSupportedAsync());
    }

    [Theory]
    [InlineData("slow-2g", EffectiveConnectionType.Slow2g)]
    [InlineData("2g", EffectiveConnectionType.TwoG)]
    [InlineData("3g", EffectiveConnectionType.ThreeG)]
    [InlineData("4g", EffectiveConnectionType.FourG)]
    [InlineData("5g", EffectiveConnectionType.Unknown)]
    [InlineData(null, EffectiveConnectionType.Unknown)]
    public async Task GetStatus_MapsEffectiveTypeAndFields(string? raw, EffectiveConnectionType expected)
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.network", new NetworkReading(raw, 7.5, 120, true));

        var status = await new NetworkInfo(js).GetStatusAsync();

        Assert.NotNull(status);
        Assert.Equal(expected, status!.EffectiveType);
        Assert.Equal(7.5, status.Downlink);
        Assert.Equal(120, status.Rtt);
        Assert.True(status.SaveData);
    }

    [Fact]
    public async Task GetStatus_ReturnsNull_WhenUnsupported()
    {
        // The helper returns null on browsers without navigator.connection (Firefox/Safari).
        var js = new FakeJsRuntime();

        Assert.Null(await new NetworkInfo(js).GetStatusAsync());
    }
}
