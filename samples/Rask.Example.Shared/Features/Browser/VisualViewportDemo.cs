using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IVisualViewport" /> — read the actually-visible viewport (size, offset, zoom).</summary>
public sealed partial class VisualViewportDemo(IVisualViewport viewport) : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Button.Class($"{Tw.BtnOutlinePrimary} mb-2").Type("button")
                    .Id("vv-read")
                    .OnClickAsync(Read)[
                    "Read visual viewport"],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Viewport: ", Code.Id("vv-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("vv-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task Read()
    {
        try
        {
            if (!await viewport.IsSupportedAsync())
            {
                _value = "not supported in this browser";
                _status = "Visual viewport unavailable";
                return;
            }

            var v = await viewport.GetAsync();
            _value = v is null
                ? "unavailable"
                : $"{v.Width:N0}×{v.Height:N0} @ scale {v.Scale:N2}, offset ({v.OffsetLeft:N0}, {v.OffsetTop:N0})";
            _status = "Viewport read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
