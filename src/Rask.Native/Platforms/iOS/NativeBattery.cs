using Foundation;
using Rask.Core.Browser;
using UIKit;

namespace Rask.Native;

// Native iOS backend for IBattery — UIDevice battery monitoring instead of navigator.getBattery, which
// WKWebView does not implement. Registered by ApplePlatform. iOS exposes only the level and charging state
// (no charge/discharge time), so those fields are null. Battery monitoring must be enabled for the level to
// read anything but -1; we enable it for the app lifetime.
internal sealed class NativeBattery : IBattery
{
    public NativeBattery() => UIDevice.CurrentDevice.BatteryMonitoringEnabled = true;

    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

    public ValueTask<BatteryStatus?> GetStatusAsync() => new(Read());

    public ValueTask<IAsyncDisposable> WatchAsync(Func<BatteryStatus, Task> onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);
        return new ValueTask<IAsyncDisposable>(new Watch(onChange));
    }

    private static BatteryStatus Read()
    {
        var device = UIDevice.CurrentDevice;
        // BatteryLevel is 0..1, or -1 when monitoring is off / the level is unknown — surface 0 for unknown.
        var level = device.BatteryLevel;
        var charging = device.BatteryState is UIDeviceBatteryState.Charging or UIDeviceBatteryState.Full;
        return new BatteryStatus(level < 0 ? 0 : level, charging, null, null);
    }

    private sealed class Watch : IAsyncDisposable
    {
        private readonly List<NSObject> _observers = [];

        public Watch(Func<BatteryStatus, Task> onChange)
        {
            UIDevice.CurrentDevice.BatteryMonitoringEnabled = true;
            var center = NSNotificationCenter.DefaultCenter;

            void Push(NSNotification note)
            {
                _ = note;
                _ = onChange(Read());
            }

            _observers.Add(center.AddObserver(UIDevice.BatteryLevelDidChangeNotification, Push));
            _observers.Add(center.AddObserver(UIDevice.BatteryStateDidChangeNotification, Push));
        }

        public ValueTask DisposeAsync()
        {
            foreach (var observer in _observers)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
            }

            _observers.Clear();
            return default;
        }
    }
}
