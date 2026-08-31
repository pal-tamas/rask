using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IVibration" /> — pulse the device's vibration motor (effective on mobile).</summary>
public sealed partial class VibrationDemo(IVibration vibration) : Component
{
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Ui.Card} shadow-sm border-0")[
            Div.Class(Ui.CardBody)[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Button.Type("button").Class(Ui.BtnOutlinePrimary)
                        .Id("vibrate-buzz")
                        .OnClickAsync(Buzz)["Buzz"],
                    Button.Type("button").Class(Ui.BtnOutlinePrimary)
                        .Id("vibrate-pattern")
                        .OnClickAsync(Pattern)[
                        "Pattern"],
                    Button.Type("button").Class(Ui.BtnOutlineDanger)
                        .Id("vibrate-cancel")
                        .OnClickAsync(Cancel)["Cancel"]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("vibrate-status")[_status ?? "(idle)"]]
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
