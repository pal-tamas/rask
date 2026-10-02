
namespace Rask.Site.Features;

/// <summary>MDN's <c>matchMedia</c> from Rask.Web — evaluate CSS media queries and user preferences from C#.</summary>
public sealed partial class MediaQueryDemo : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Ui.Button.Primary.Outline.Class("mb-2")
                    .Id("media-read")
                    .OnClick(Read)["Evaluate media queries"],
                Div.Class("text-sm text-ui-muted")["Result: ", Code.Id("media-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("media-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            var wide = await Window.MatchMedia("(min-width: 768px)").Matches;
            var dark = await Window.MatchMedia("(prefers-color-scheme: dark)").Matches;
            var reduced = await Window.MatchMedia("(prefers-reduced-motion: reduce)").Matches;
            _value = $"≥768px: {wide}, prefersDark: {dark}, reducedMotion: {reduced}";
            _status = "Media queries evaluated";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
