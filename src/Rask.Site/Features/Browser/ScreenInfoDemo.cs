using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary><see cref="IScreenInfo" /> — read the display size, color depth, and device pixel ratio.</summary>
public sealed partial class ScreenInfoDemo(IScreenInfo screen) : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        UiCard.Class("shadow-sm")[
                UiButton.Tone(UiTone.Primary).Variant(UiVariant.Outline).Class("mb-2")
                    .Id("screen-read")
                    .OnClick(Read)["Read screen info"],
                Div.Class("text-sm text-ui-muted")["Display: ", Code.Id("screen-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("screen-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            var s = await screen.GetAsync();
            _value = $"{s.Width}×{s.Height} (avail {s.AvailWidth}×{s.AvailHeight}), {s.ColorDepth}-bit, DPR {s.PixelRatio}";
            _status = "Screen read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
