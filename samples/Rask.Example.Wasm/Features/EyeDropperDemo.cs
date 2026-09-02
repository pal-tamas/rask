using Rask.Example.Shared;
using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IEyeDropper" /> — pick a color from anywhere on screen with the system loupe, then show
///     the picked swatch + hex. WASM-only: <c>open()</c> needs a live user gesture.
/// </summary>
public sealed partial class EyeDropperDemo(IEyeDropper eyeDropper) : Component
{
    private string? _hex;
    private string _status = "(idle)";

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("flex items-center gap-3 mb-2")[
                    Button.Class(Tw.BtnPrimary).Id("eyedropper-pick").OnClickAsync(Pick)[
                        Icon.Name(IconName.Eyedropper).Class("me-1"), "Pick a color"],
                    _hex is null
                        ? (Component)Span.Class("text-slate-500 dark:text-slate-400 text-sm")["No color picked yet"]
                        : Div.Class("flex items-center gap-2")[
                            Span
                                .Id("eyedropper-swatch")
                                .Class("inline-block rounded border")
                                .Style($"width: 2rem; height: 2rem; background: {_hex}"),
                            Code.Id("eyedropper-hex")[_hex]
                        ]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("eyedropper-status")[_status]]
            ]
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
