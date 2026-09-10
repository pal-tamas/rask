using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary><see cref="INavigatorInfo" /> — read-only navigator facts (online, language, user agent).</summary>
public sealed partial class NavigatorInfoDemo(INavigatorInfo navigator) : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        UiCard.Class("shadow-sm")[
                UiButton.Label("Read navigator info").Tone(UiTone.Primary).Variant(UiVariant.Outline).Class("mb-2")
                    .Id("nav-read")
                    .OnClick(Read),
                Div.Class("text-sm text-ui-muted")["Info: ", Code.Id("nav-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("nav-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            var online = await navigator.OnLineAsync();
            var language = await navigator.LanguageAsync();
            _value = $"online: {online}, language: {language}";
            _status = "Navigator read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
