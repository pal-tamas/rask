using System.Globalization;
using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>deviceorientation</c> and <c>devicemotion</c> events from Rask.Web — read the gyroscope/compass tilt
///     and the accelerometer. Tap <b>Start</b> (which asks for sensor permission inside the click, as iOS requires),
///     then tilt or shake the device: each reading re-renders the readout. Sensors only fire on a real device with
///     motion hardware.
/// </summary>
public sealed partial class DeviceSensorsDemo : Component
{
    private IAsyncDisposable? _orientationWatch;
    private IAsyncDisposable? _motionWatch;
    private string _status = "(idle)";
    private Types.DeviceOrientationEvent? _tilt;
    private Types.DeviceMotionEventAcceleration? _accel;

    private async Task Start()
    {
        try
        {
            if (!await DeviceOrientationEvent.IsSupported)
            {
                _status = "Device orientation not supported";
                return;
            }

            // Ask for both before subscribing: iOS only honours requestPermission() while the click is still live.
            var orientationAllowed = await Allowed(DeviceOrientationEvent.RequestPermission);
            var motionAllowed = await Allowed(DeviceMotionEvent.RequestPermission);
            if (!orientationAllowed)
            {
                _status = "Permission denied";
                return;
            }

            // The sensors fire ~60 times a second; ten readings (the latest each time) is plenty for a readout.
            _orientationWatch ??= await Window.OnDeviceOrientation(e => _tilt = e, every: 100.Milliseconds);
            if (motionAllowed)
            {
                _motionWatch ??= await Window.OnDeviceMotion(e => _accel = e.Acceleration, every: 100.Milliseconds);
            }

            _status = "listening — tilt or shake the device";
        }
        catch (JSException ex)
        {
            _status = "start failed: " + ex.Message;
        }
    }

    // Only iOS asks: elsewhere requestPermission() does not exist, and the sensors just fire.
    private static async Task<bool> Allowed(Func<ValueTask<Types.PermissionState>> request)
    {
        try
        {
            return await request() is Types.PermissionState.Granted;
        }
        catch (JSException ex) when (ex.Message.Contains("is not a function", StringComparison.Ordinal))
        {
            return true;
        }
    }

    protected override Component? Render() =>
        Ui.Card[
                Ui.Button.Primary.Class("mb-3").Id("sensor-start").OnClick(Start)["Start"],
                Div.Class("text-sm text-ui-muted mb-2")["Status: ", Code.Id("sensor-status")[_status]],
                Div.Class("grid grid-cols-12 gap-4")[
                    Div.Class("col-span-12 sm:col-span-6")[
                        Div.Class("font-semibold text-sm mb-1")["Orientation (°)"],
                        Div.Class("text-sm text-ui-muted")[
                            "α ", Code.Id("sensor-alpha")[Fmt(_tilt?.Alpha)],
                            " · β ", Code.Id("sensor-beta")[Fmt(_tilt?.Beta)],
                            " · γ ", Code.Id("sensor-gamma")[Fmt(_tilt?.Gamma)]]
                    ],
                    Div.Class("col-span-12 sm:col-span-6")[
                        Div.Class("font-semibold text-sm mb-1")["Acceleration (m/s²)"],
                        Div.Class("text-sm text-ui-muted")[
                            "x ", Code.Id("sensor-ax")[Fmt(_accel?.X)],
                            " · y ", Code.Id("sensor-ay")[Fmt(_accel?.Y)],
                            " · z ", Code.Id("sensor-az")[Fmt(_accel?.Z)]]
                    ]
                ]
            ];

    private static string Fmt(double? value) => value is null ? "—" : value.Value.ToString("0.0", CultureInfo.InvariantCulture);

    protected override async Task OnUnmount()
    {
        if (_orientationWatch is not null)
        {
            await _orientationWatch.DisposeAsync();
        }

        if (_motionWatch is not null)
        {
            await _motionWatch.DisposeAsync();
        }
    }
}
