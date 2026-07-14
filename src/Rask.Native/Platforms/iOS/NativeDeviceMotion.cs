using CoreMotion;
using Foundation;
using Rask.Core.Browser;

namespace Rask.Native;

// Native iOS backend for IDeviceMotion — CoreMotion (CMMotionManager) instead of the WebView's devicemotion
// events. UserAcceleration excludes gravity (matching the web `acceleration`) and is in g, converted to
// m/s²; RotationRate is rad/s, converted to °/s. Registered by ApplePlatform; no permission needed on iOS.
internal sealed class NativeDeviceMotion : IDeviceMotion
{
    private const double GravityMetersPerSecondSquared = 9.80665;

    public ValueTask<bool> IsSupportedAsync() =>
        ValueTask.FromResult(new CMMotionManager().DeviceMotionAvailable);

    public ValueTask<SensorPermission> RequestPermissionAsync() =>
        ValueTask.FromResult(SensorPermission.Granted);

    public ValueTask<IAsyncDisposable> WatchAsync(Func<MotionReading, Task> onReading)
    {
        ArgumentNullException.ThrowIfNull(onReading);
        return new ValueTask<IAsyncDisposable>(new Watch(onReading));
    }

    private sealed class Watch : IAsyncDisposable
    {
        private readonly CMMotionManager _mgr = new() { DeviceMotionUpdateInterval = 1.0 / 60 };

        public Watch(Func<MotionReading, Task> onReading)
        {
            if (!_mgr.DeviceMotionAvailable)
            {
                return;
            }

            var intervalMs = _mgr.DeviceMotionUpdateInterval * 1000;
            _mgr.StartDeviceMotionUpdates(NSOperationQueue.CurrentQueue ?? new NSOperationQueue(), (motion, err) =>
            {
                _ = err;
                if (motion is null)
                {
                    return;
                }

                var acc = motion.UserAcceleration; // g, gravity excluded
                var rot = motion.RotationRate;      // rad/s
                _ = onReading(new MotionReading(
                    AccelerationX: acc.X * GravityMetersPerSecondSquared,
                    AccelerationY: acc.Y * GravityMetersPerSecondSquared,
                    AccelerationZ: acc.Z * GravityMetersPerSecondSquared,
                    RotationAlpha: RadToDeg(rot.z),
                    RotationBeta: RadToDeg(rot.x),
                    RotationGamma: RadToDeg(rot.y),
                    Interval: intervalMs));
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
