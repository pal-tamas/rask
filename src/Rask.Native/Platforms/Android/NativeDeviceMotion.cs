using Android.App;
using Android.Content;
using Android.Hardware;
using Android.Runtime;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for IDeviceMotion — SensorManager's linear accelerometer (gravity excluded, m/s²,
// matching the web `acceleration`) plus the gyroscope (rad/s → °/s), instead of the WebView's devicemotion
// events. Registered by AndroidPlatform; no runtime permission needed for these sensors.
internal sealed class NativeDeviceMotion(Activity activity) : IDeviceMotion
{
    public ValueTask<bool> IsSupportedAsync() =>
        ValueTask.FromResult(Manager()?.GetDefaultSensor(SensorType.Accelerometer) is not null);

    public ValueTask<SensorPermission> RequestPermissionAsync() =>
        ValueTask.FromResult(SensorPermission.Granted);

    public ValueTask<IAsyncDisposable> WatchAsync(Func<MotionReading, Task> onReading)
    {
        ArgumentNullException.ThrowIfNull(onReading);
        return new ValueTask<IAsyncDisposable>(new Watch(Manager(), onReading));
    }

    private SensorManager? Manager() => (SensorManager?)activity.GetSystemService(Context.SensorService);

    private sealed class Watch : Java.Lang.Object, ISensorEventListener, IAsyncDisposable
    {
        private readonly SensorManager? _mgr;
        private readonly Func<MotionReading, Task> _onReading;
        private double _ax, _ay, _az, _ralpha, _rbeta, _rgamma;

        public Watch(SensorManager? mgr, Func<MotionReading, Task> onReading)
        {
            _mgr = mgr;
            _onReading = onReading;
            // Prefer linear acceleration (gravity removed) to match the web `acceleration`; fall back to the
            // raw accelerometer where the fused sensor isn't present.
            var accel = mgr?.GetDefaultSensor(SensorType.LinearAcceleration)
                        ?? mgr?.GetDefaultSensor(SensorType.Accelerometer);
            var gyro = mgr?.GetDefaultSensor(SensorType.Gyroscope);
            if (accel is not null)
            {
                mgr!.RegisterListener(this, accel, SensorDelay.Game);
            }

            if (gyro is not null)
            {
                mgr!.RegisterListener(this, gyro, SensorDelay.Game);
            }
        }

        public void OnAccuracyChanged(Sensor? sensor, [GeneratedEnum] SensorStatus accuracy) { }

        public void OnSensorChanged(SensorEvent? e)
        {
            if (e?.Sensor is null || e.Values is null || e.Values.Count < 3)
            {
                return;
            }

            switch (e.Sensor.Type)
            {
                case SensorType.LinearAcceleration or SensorType.Accelerometer:
                    _ax = e.Values[0];
                    _ay = e.Values[1];
                    _az = e.Values[2];
                    break;
                case SensorType.Gyroscope:
                    _ralpha = RadToDeg(e.Values[2]);
                    _rbeta = RadToDeg(e.Values[0]);
                    _rgamma = RadToDeg(e.Values[1]);
                    break;
            }

            _ = _onReading(new MotionReading(_ax, _ay, _az, _ralpha, _rbeta, _rgamma, null));
        }

        public ValueTask DisposeAsync()
        {
            _mgr?.UnregisterListener(this);
            return default;
        }
    }

    private static double RadToDeg(double radians) => radians * 180.0 / Math.PI;
}
