using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="INetworkInfo" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>navigator.connection</c> is a live, vendor-prefixed object, so the read goes through the
///     framework's <c>__raskApi.network</c> helper, which returns a plain
///     <c>{ effectiveType, downlink, rtt, saveData }</c> snapshot (mapped to <see cref="NetworkStatus" />).
/// </summary>
public sealed class NetworkInfo(IJSRuntime js) : INetworkInfo
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskApi.networkSupported");

    /// <inheritdoc />
    public async ValueTask<NetworkStatus?> GetStatusAsync()
    {
        var reading = await js.InvokeAsync<NetworkReading?>("__raskApi.network");
        return reading is null
            ? null
            : new NetworkStatus(MapType(reading.EffectiveType), reading.Downlink, reading.Rtt, reading.SaveData);
    }

    private static EffectiveConnectionType MapType(string? type) => type switch
    {
        "slow-2g" => EffectiveConnectionType.Slow2g,
        "2g" => EffectiveConnectionType.TwoG,
        "3g" => EffectiveConnectionType.ThreeG,
        "4g" => EffectiveConnectionType.FourG,
        _ => EffectiveConnectionType.Unknown
    };
}
