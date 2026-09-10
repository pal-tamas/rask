using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary><see cref="IMediaQuery" /> — evaluate CSS media queries and user preferences from C#.</summary>
public sealed partial class MediaQueryDemo(IMediaQuery media) : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                UiButton.Label("Evaluate media queries").Tone(UiTone.Primary).Variant(UiVariant.Outline).Class("mb-2")
                    .Id("media-read")
                    .OnClick(Read),
                Div.Class("text-sm text-ui-muted")["Result: ", Code.Id("media-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("media-status")[_status ?? "(idle)"]]
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
