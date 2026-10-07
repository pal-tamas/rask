
namespace Rask.Site.Features;

/// <summary>MDN's Screen and <c>devicePixelRatio</c> from Rask.Web — read the display size, color depth, and pixel ratio.</summary>
public sealed partial class ScreenInfoDemo : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Ui.Button.Class("mb-2")
                    .Id("screen-read")
                    .OnClick(Read)["Read screen info"],
                Div.Class("text-sm text-ui-muted")["Display: ", Code.Id("screen-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("screen-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            var s = Window.Screen;
            _value = $"{await s.Width}×{await s.Height} (avail {await s.AvailWidth}×{await s.AvailHeight}), "
                + $"{await s.ColorDepth}-bit, DPR {await Window.DevicePixelRatio}";
            _status = "Screen read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
