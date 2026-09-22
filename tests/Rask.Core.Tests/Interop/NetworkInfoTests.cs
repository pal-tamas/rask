using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class NetworkInfoTests
{
    [Fact]
    public async Task Asking_whether_network_info_is_supported_calls_the_helper()
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
    public async Task Getting_the_status_maps_the_effective_type_and_fields(string? raw, EffectiveConnectionType expected)
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
    public async Task Getting_the_status_gives_null_when_unsupported()
    {
        // The helper returns null on browsers without navigator.connection (Firefox/Safari).
        var js = new FakeJsRuntime();

        Assert.Null(await new NetworkInfo(js).GetStatusAsync());
    }
}
