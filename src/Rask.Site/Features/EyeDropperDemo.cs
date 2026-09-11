using Rask.Site;
using Rask.Wasm.Browser;

namespace Rask.Site.Features;

/// <summary>
///     <see cref="IEyeDropper" /> — pick a color from anywhere on screen with the system loupe, then show
///     the picked swatch + hex. WASM-only: <c>open()</c> needs a live user gesture.
/// </summary>
public sealed partial class EyeDropperDemo(IEyeDropper eyeDropper) : Component
{
    private string? _hex;
    private string _status = "(idle)";

    protected override Component? Render() =>
        UiCard.Class("shadow-sm")[
                Div.Class("flex items-center gap-3 mb-2")[
                    UiButton.Tone(UiTone.Primary).Id("eyedropper-pick").OnClick(Pick)[UiIcon.Name(UiIconName.EyeDropper), "Pick a color"],
                    _hex is null
                        ? (Component)Span.Class("text-ui-muted text-sm")["No color picked yet"]
                        : Div.Class("flex items-center gap-2")[
                            Span
                                .Id("eyedropper-swatch")
                                .Class("inline-block rounded border")
                                .Style($"width: 2rem; height: 2rem; background: {_hex}"),
                            Code.Id("eyedropper-hex")[_hex]
                        ]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("eyedropper-status")[_status]]
            ];

    private async Task Pick()
    {
        try
        {
            if (!await eyeDropper.IsSupportedAsync())
            {
                _status = "EyeDropper not supported in this browser";
                return;
            }

            var hex = await eyeDropper.OpenAsync();
            if (hex is null)
            {
                _status = "Cancelled";
                return;
            }

            _hex = hex;
            _status = "Picked " + hex;
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }
}
