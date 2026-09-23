using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary><see cref="IVibration" /> — pulse the device's vibration motor (effective on mobile).</summary>
public sealed partial class VibrationDemo(IVibration vibration) : Component
{
    private string? _status;

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Tone(Ui.Tone.Primary).Variant(Ui.Variant.Outline)
                        .Id("vibrate-buzz")
                        .OnClick(Buzz)["Buzz"],
                    Ui.Button.Tone(Ui.Tone.Primary).Variant(Ui.Variant.Outline)
                        .Id("vibrate-pattern")
                        .OnClick(Pattern)["Pattern"],
                    Ui.Button.Tone(Ui.Tone.Error).Variant(Ui.Variant.Outline)
                        .Id("vibrate-cancel")
                        .OnClick(Cancel)["Cancel"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("vibrate-status")[_status ?? "(idle)"]]
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
