using Android.App;
using Android.Content;
using Android.Hardware;
using Android.Runtime;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for IDeviceOrientation — SensorManager's TYPE_ROTATION_VECTOR (fused compass +
// gyro), converted to the web deviceorientation alpha/beta/gamma, instead of the WebView's deviceorientation
// events. Registered by AndroidPlatform. Android needs no runtime permission for these sensors.
internal sealed class NativeDeviceOrientation(Activity activity) : IDeviceOrientation
{
    public ValueTask<bool> IsSupportedAsync() =>
        ValueTask.FromResult(Manager()?.GetDefaultSensor(SensorType.RotationVector) is not null);

    public ValueTask<SensorPermission> RequestPermissionAsync() =>
        ValueTask.FromResult(SensorPermission.Granted);

    public ValueTask<IAsyncDisposable> WatchAsync(Func<OrientationReading, Task> onReading)
    {
        ArgumentNullException.ThrowIfNull(onReading);
        return new ValueTask<IAsyncDisposable>(new Watch(Manager(), onReading));
    }

    private SensorManager? Manager() => (SensorManager?)activity.GetSystemService(Context.SensorService);

    private sealed class Watch : Java.Lang.Object, ISensorEventListener, IAsyncDisposable
    {
        private readonly SensorManager? _mgr;
        private readonly Func<OrientationReading, Task> _onReading;

        public Watch(SensorManager? mgr, Func<OrientationReading, Task> onReading)
        {
            _mgr = mgr;
            _onReading = onReading;
            var sensor = mgr?.GetDefaultSensor(SensorType.RotationVector);
            if (sensor is not null)
            {
                mgr!.RegisterListener(this, sensor, SensorDelay.Ui);
            }
        }

        public void OnAccuracyChanged(Sensor? sensor, [GeneratedEnum] SensorStatus accuracy) { }

        public void OnSensorChanged(SensorEvent? e)
        {
            if (e?.Values is null || e.Values.Count < 4)
            {
                return;
            }

            var vector = new float[4];
            for (var i = 0; i < 4; i++)
            {
                vector[i] = e.Values[i];
            }

            var matrix = new float[9];
            SensorManager.GetRotationMatrixFromVector(matrix, vector);
            var angles = new float[3]; // [azimuth, pitch, roll] radians
            SensorManager.GetOrientation(matrix, angles);

            _ = _onReading(new OrientationReading(
                Alpha: (RadToDeg(angles[0]) + 360) % 360, // compass heading, 0–360
                Beta: RadToDeg(angles[1]),
                Gamma: RadToDeg(angles[2]),
                Absolute: true));
        }

        public ValueTask DisposeAsync()
        {
            _mgr?.UnregisterListener(this);
            return default;
        }
    }

    private static double RadToDeg(double radians) => radians * 180.0 / Math.PI;
}
