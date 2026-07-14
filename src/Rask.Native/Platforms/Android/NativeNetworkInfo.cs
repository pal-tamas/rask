using Android.App;
using Android.Content;
using Android.Net;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for INetworkInfo — ConnectivityManager (needs ACCESS_NETWORK_STATE) instead of
// navigator.connection. Maps the active network's transport + reported downstream bandwidth to the typed
// snapshot. Registered by AndroidPlatform.
internal sealed class NativeNetworkInfo(Activity activity) : INetworkInfo
{
    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

    public ValueTask<NetworkStatus?> GetStatusAsync()
    {
        var cm = (ConnectivityManager?)activity.GetSystemService(Context.ConnectivityService);
        var network = cm?.ActiveNetwork;
        var caps = network is null ? null : cm!.GetNetworkCapabilities(network);
        if (caps is null)
        {
            return ValueTask.FromResult<NetworkStatus?>(
                new NetworkStatus(EffectiveConnectionType.Unknown, 0, 0, false));
        }

        var fast = caps.HasTransport(TransportType.Wifi) || caps.HasTransport(TransportType.Ethernet);
        var downKbps = caps.LinkDownstreamBandwidthKbps;
        var effective = fast ? EffectiveConnectionType.FourG : ClassifyCellular(downKbps);
        var saveData = OperatingSystem.IsAndroidVersionAtLeast(24)
            && cm!.RestrictBackgroundStatus == RestrictBackgroundStatus.Enabled;
        return ValueTask.FromResult<NetworkStatus?>(
            new NetworkStatus(effective, downKbps / 1000.0, 0, saveData));
    }

    private static EffectiveConnectionType ClassifyCellular(int downKbps) => downKbps switch
    {
        <= 0 => EffectiveConnectionType.Unknown,
        < 100 => EffectiveConnectionType.Slow2g,
        < 300 => EffectiveConnectionType.TwoG,
        < 1500 => EffectiveConnectionType.ThreeG,
        _ => EffectiveConnectionType.FourG
    };
}
