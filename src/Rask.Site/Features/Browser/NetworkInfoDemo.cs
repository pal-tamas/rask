
namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>navigator.connection</c>, from Rask.Web — read the connection quality (effective type, downlink, Data
///     Saver). Chromium only.
/// </summary>
public sealed partial class NetworkInfoDemo : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Ui.Button.Primary.Outline.Class("mb-2")
                    .Id("net-read")
                    .OnClick(Read)["Read network status"],
                Div.Class("text-sm text-ui-muted")["Connection: ", Code.Id("net-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("net-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            var connection = Navigator.Connection;
            if (!await connection.IsSupported)
            {
                _value = "not supported (try a Chromium browser)";
                _status = "Network Information unavailable";
                return;
            }

            var type = (await connection.EffectiveType).ToString().TrimStart('_');   // MDN's "4g" is the enum's _4g
            _value = $"{type}, {await connection.Downlink} Mbps, {await connection.Rtt} ms RTT, saveData: {await connection.SaveData}";
            _status = "Network read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
