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
        Div.Class("card shadow-sm border-0")[
            Div.Class("card-body")[
                Div.Class("d-flex align-items-center gap-3 mb-2")[
                    Button.Class("btn btn-primary btn-sm").Id("eyedropper-pick").OnClickAsync(Pick)[
                        I.Class("bi bi-eyedropper me-1"), "Pick a color"],
                    _hex is null
                        ? (Component)Span.Class("text-secondary small")["No color picked yet"]
                        : Div.Class("d-flex align-items-center gap-2")[
                            Span
                                .Id("eyedropper-swatch")
                                .Class("d-inline-block rounded border")
                                .Style($"width: 2rem; height: 2rem; background: {_hex}"),
                            Code.Id("eyedropper-hex")[_hex]
                        ]
                ],
                Div.Class("small text-secondary")["Status: ", Code.Id("eyedropper-status")[_status]]
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
