using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IMediaQuery" /> — evaluate CSS media queries and user preferences from C#.</summary>
public sealed partial class MediaQueryDemo(IMediaQuery media) : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Ui.Card} shadow-sm border-0")[
            Div.Class(Ui.CardBody)[
                Button.Class($"{Ui.BtnOutlinePrimary} mb-2").Type("button")
                    .Id("media-read")
                    .OnClickAsync(Read)[
                    "Evaluate media queries"],
                Div.Class("small text-secondary")["Result: ", Code.Id("media-value")[_value ?? "(not requested)"]],
                Div.Class("small text-secondary")["Status: ", Code.Id("media-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task Read()
    {
        try
        {
            var wide = await media.MatchesAsync("(min-width: 768px)");
            var dark = await media.PrefersDarkAsync();
            var reduced = await media.PrefersReducedMotionAsync();
            _value = $"≥768px: {wide}, prefersDark: {dark}, reducedMotion: {reduced}";
            _status = "Media queries evaluated";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
