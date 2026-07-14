using CoreFoundation;
using Network;
using Rask.Core.Browser;

namespace Rask.Native;

// Native iOS backend for INetworkInfo — NWPathMonitor (Network.framework) instead of navigator.connection,
// which iOS/WebKit does not implement. One-shot: read the first path snapshot, then cancel the monitor. iOS
// doesn't surface a 2g/3g/4g class or downlink/rtt to apps, so the reachability + interface type are mapped
// to an approximate EffectiveConnectionType and bandwidth/rtt are left at 0. Registered by ApplePlatform.
internal sealed class NativeNetworkInfo : INetworkInfo
{
    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

    public ValueTask<NetworkStatus?> GetStatusAsync()
    {
        var tcs = new TaskCompletionSource<NetworkStatus?>();
        var monitor = new NWPathMonitor();
        monitor.SnapshotHandler = path =>
        {
            var status = Map(path);
            monitor.Cancel();
            tcs.TrySetResult(status);
        };
        monitor.SetQueue(DispatchQueue.DefaultGlobalQueue);
        monitor.Start();
        return new ValueTask<NetworkStatus?>(tcs.Task);
    }

    private static NetworkStatus Map(NWPath path)
    {
        var online = path.Status == NWPathStatus.Satisfied;
        var wired = path.UsesInterfaceType(NWInterfaceType.Wifi) || path.UsesInterfaceType(NWInterfaceType.Wired);
        var effective = !online
            ? EffectiveConnectionType.Unknown
            : wired
                ? EffectiveConnectionType.FourG      // Wi-Fi / Ethernet: treat as fast
                : EffectiveConnectionType.ThreeG;    // cellular: iOS won't say the generation — approximate
        return new NetworkStatus(effective, 0, 0, path.IsConstrained);
    }
}
