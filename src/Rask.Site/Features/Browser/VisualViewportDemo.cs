
namespace Rask.Site.Features;

/// <summary>MDN's VisualViewport from Rask.Web — read the actually-visible viewport (size, offset, zoom).</summary>
public sealed partial class VisualViewportDemo : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Ui.Button.Class("mb-2")
                    .Id("vv-read")
                    .OnClick(Read)["Read visual viewport"],
                Div.Class("text-sm text-ui-muted")["Viewport: ", Code.Id("vv-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("vv-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            var v = Window.VisualViewport;
            if (!await v.IsSupported)
            {
                _value = "not supported in this browser";
                _status = "Visual viewport unavailable";
                return;
            }

            _value = $"{await v.Width:N0}×{await v.Height:N0} @ scale {await v.Scale:N2}, "
                + $"offset ({await v.OffsetLeft:N0}, {await v.OffsetTop:N0})";
            _status = "Viewport read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
