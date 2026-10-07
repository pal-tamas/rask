
namespace Rask.Site.Features;

/// <summary>
///     <c>EyeDropper</c> (Rask.Web) — pick a color from anywhere on screen with the system loupe, then show
///     the picked swatch + hex. WASM-only: <c>open()</c> needs a live user gesture.
/// </summary>
public sealed partial class EyeDropperDemo : Component
{
    private string? _hex;
    private string _status = "(idle)";

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex items-center gap-3 mb-2")[
                    Ui.Button.Primary.Icon(Ui.IconName.EyeDropper).Id("eyedropper-pick").OnClick(Pick)["Pick a color"],
                    _hex is null
                        ? Span.Class("text-ui-muted text-sm")["No color picked yet"]
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

    // new EyeDropper().open(): rejects when the browser has no EyeDropper, or when the user presses Escape.
    private async Task Pick()
    {
        try
        {
            await using var dropper = await EyeDropper.Create();
            var color = await dropper.Open();
            _hex = color.SRGBHex;
            _status = "Picked " + _hex;
        }
        catch (Exception ex)
        {
            _status = "No color picked: " + ex.Message;
        }
    }
}
