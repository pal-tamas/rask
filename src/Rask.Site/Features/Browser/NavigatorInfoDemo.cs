
namespace Rask.Site.Features;

/// <summary>MDN's Navigator from Rask.Web — read-only navigator facts (online, language).</summary>
public sealed partial class NavigatorInfoDemo : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Ui.Button.Primary.Outline.Class("mb-2")
                    .Id("nav-read")
                    .OnClick(Read)["Read navigator info"],
                Div.Class("text-sm text-ui-muted")["Info: ", Code.Id("nav-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("nav-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            var online = await Navigator.OnLine;
            var language = await Navigator.Language;
            _value = $"online: {online}, language: {language}";
            _status = "Navigator read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
