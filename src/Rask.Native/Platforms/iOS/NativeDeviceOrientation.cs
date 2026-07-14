using CoreMotion;
using Foundation;
using Rask.Core.Browser;

namespace Rask.Native;

// Native iOS backend for IDeviceOrientation — CoreMotion (CMMotionManager device-motion attitude) instead of
// the WebView's deviceorientation events, which WKWebView gates/withholds. Registered by ApplePlatform. iOS
// needs no separate permission for attitude, so RequestPermissionAsync returns Granted. Absolute is true
// (device-motion attitude is referenced to a fixed frame). Angles are radians → degrees.
internal sealed class NativeDeviceOrientation : IDeviceOrientation
{
    public ValueTask<bool> IsSupportedAsync() =>
        ValueTask.FromResult(new CMMotionManager().DeviceMotionAvailable);

    public ValueTask<SensorPermission> RequestPermissionAsync() =>
        ValueTask.FromResult(SensorPermission.Granted);

    public ValueTask<IAsyncDisposable> WatchAsync(Func<OrientationReading, Task> onReading)
    {
        ArgumentNullException.ThrowIfNull(onReading);
        return new ValueTask<IAsyncDisposable>(new Watch(onReading));
    }

    private sealed class Watch : IAsyncDisposable
    {
        private readonly CMMotionManager _mgr = new() { DeviceMotionUpdateInterval = 1.0 / 60 };

        public Watch(Func<OrientationReading, Task> onReading)
        {
            if (!_mgr.DeviceMotionAvailable)
            {
                return;
            }

            _mgr.StartDeviceMotionUpdates(NSOperationQueue.CurrentQueue ?? new NSOperationQueue(), (motion, err) =>
            {
                _ = err;
                if (motion?.Attitude is not { } attitude)
                {
                    return;
                }

                // Map CoreMotion yaw/pitch/roll to the web deviceorientation alpha/beta/gamma.
                _ = onReading(new OrientationReading(
                    Alpha: RadToDeg(attitude.Yaw),
                    Beta: RadToDeg(attitude.Pitch),
                    Gamma: RadToDeg(attitude.Roll),
                    Absolute: true));
            });
        }

        public ValueTask DisposeAsync()
        {
            _mgr.StopDeviceMotionUpdates();
            return default;
        }
    }

    private static double RadToDeg(double radians) => radians * 180.0 / Math.PI;
}
