
namespace Rask.Site.Features;

/// <summary>MDN's <c>navigator.vibrate</c>, from Rask.Web — pulse the device's vibration motor (effective on mobile).</summary>
public sealed partial class VibrationDemo : Component
{
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Primary.Outline
                        .Id("vibrate-buzz")
                        .OnClick(() => Vibrate([200], "Vibrated"))["Buzz"],
                    Ui.Button.Primary.Outline
                        .Id("vibrate-pattern")
                        .OnClick(() => Vibrate([100, 50, 100, 50, 300], "Pattern played"))["Pattern"],
                    Ui.Button.Error.Outline
                        .Id("vibrate-cancel")
                        .OnClick(() => Vibrate([0], "Cancelled"))["Cancel"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("vibrate-status")[_status ?? "(idle)"]]
            ];

    // A pattern of 0 cancels. A browser without navigator.vibrate (Safari, Firefox) fails the call outright.
    private async Task Vibrate(int[] pattern, string done)
    {
        try
        {
            _status = await Navigator.Vibrate(pattern) ? done : "Not supported on this device";
        }
        catch (Exception) { _status = "Not supported on this device"; }
    }
}
