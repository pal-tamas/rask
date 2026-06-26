using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IScreenInfo" /> — read the display size, color depth, and device pixel ratio.</summary>
public sealed class ScreenInfoDemo(IScreenInfo screen) : Component
{
    private string? _value;
    private string? _status;

    protected override RenderResult Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Button(Class: "btn btn-outline-primary btn-sm mb-2", Id: "screen-read", OnClickAsync: Read)[
                    "Read screen info"],
                Div(Class: "small text-secondary")["Display: ", Code(Id: "screen-value")[_value ?? "(not requested)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "screen-status")[_status ?? "(idle)"]]
            ]
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
