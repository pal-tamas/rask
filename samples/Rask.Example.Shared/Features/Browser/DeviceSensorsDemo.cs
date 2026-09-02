using Rask.Core;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IDeviceOrientation" /> + <see cref="IDeviceMotion" /> — read the gyroscope/compass tilt
///     and the accelerometer. Tap <b>Start</b> (which requests sensor permission from the gesture, required
///     on iOS), then tilt or shake the device: the browser pushes each reading to C#, which updates the
///     readout (the handler calls <c>StateHasChanged()</c>, the sanctioned pattern for an externally-pushed
///     update). Sensors only emit on a real device with motion hardware.
/// </summary>
public sealed partial class DeviceSensorsDemo(IDeviceOrientation orientation, IDeviceMotion motion)
    : Component, IAsyncDisposable
{
    private IAsyncDisposable? _orientationWatch;
    private IAsyncDisposable? _motionWatch;
    private string _status = "(idle)";
    private OrientationReading? _tilt;
    private MotionReading? _accel;

    private async Task Start()
    {
        try
        {
            if (!await orientation.IsSupportedAsync())
            {
                _status = "Device orientation not supported";
                return;
            }

            // Request both permissions up front, before any WatchAsync — iOS only honours
            // requestPermission() while the click's user activation is still live, so the motion request
            // must not wait behind the orientation watch.
            var orientationGranted = await orientation.RequestPermissionAsync() == SensorPermission.Granted;
            var motionGranted = await motion.RequestPermissionAsync() == SensorPermission.Granted;

            if (!orientationGranted)
            {
                _status = "Permission denied";
                return;
            }

            _orientationWatch ??= await orientation.WatchAsync(r =>
            {
                _tilt = r;
                StateHasChanged();
                return Task.CompletedTask;
            });

            if (motionGranted)
            {
                _motionWatch ??= await motion.WatchAsync(r =>
                {
                    _accel = r;
                    StateHasChanged();
                    return Task.CompletedTask;
                });
            }

            _status = "listening — tilt or shake the device";
        }
        catch (Exception ex)
        {
            _status = "start failed: " + ex.Message;
        }
    }

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Button.Class($"{Tw.BtnPrimary} mb-3").Id("sensor-start").OnClickAsync(Start)["Start"],
                Div.Class("text-sm text-slate-500 dark:text-slate-400 mb-2")["Status: ", Code.Id("sensor-status")[_status]],
                Div.Class("grid grid-cols-12 gap-4")[
                    Div.Class("sm:col-span-6")[
                        Div.Class("font-semibold text-sm mb-1")["Orientation (°)"],
                        Div.Class("text-sm text-slate-500 dark:text-slate-400")[
                            "α ", Code.Id("sensor-alpha")[Fmt(_tilt?.Alpha)],
                            " · β ", Code.Id("sensor-beta")[Fmt(_tilt?.Beta)],
                            " · γ ", Code.Id("sensor-gamma")[Fmt(_tilt?.Gamma)]]
                    ],
                    Div.Class("sm:col-span-6")[
                        Div.Class("font-semibold text-sm mb-1")["Acceleration (m/s²)"],
                        Div.Class("text-sm text-slate-500 dark:text-slate-400")[
                            "x ", Code.Id("sensor-ax")[Fmt(_accel?.AccelerationX)],
                            " · y ", Code.Id("sensor-ay")[Fmt(_accel?.AccelerationY)],
                            " · z ", Code.Id("sensor-az")[Fmt(_accel?.AccelerationZ)]]
                    ]
                ]
            ]
        ];

    private static string Fmt(double? value) => value is null ? "—" : value.Value.ToString("0.0");

    public async ValueTask DisposeAsync()
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
