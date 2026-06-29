using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IVibration" /> — pulse the device's vibration motor (effective on mobile).</summary>
public sealed class VibrationDemo(IVibration vibration) : Component
{
    private string? _status;

    protected override RenderResult Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "vibrate-buzz", OnClickAsync: Buzz)["Buzz"],
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "vibrate-pattern", OnClickAsync: Pattern)[
                        "Pattern"],
                    Button(Class: "btn btn-outline-danger btn-sm", Id: "vibrate-cancel", OnClickAsync: Cancel)["Cancel"]
                ],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "vibrate-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task Buzz()
    {
        var ok = await vibration.VibrateAsync(200);
        _status = ok ? "Vibrated" : "Not supported on this device";
    }

    private async Task Pattern()
    {
        var ok = await vibration.VibrateAsync(100, 50, 100, 50, 300);
        _status = ok ? "Pattern played" : "Not supported on this device";
    }

    private async Task Cancel()
    {
        await vibration.CancelAsync();
        _status = "Cancelled";
    }
}
