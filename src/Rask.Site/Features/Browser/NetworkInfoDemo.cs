using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary><see cref="INetworkInfo" /> — read the connection quality (effective type, downlink, Data Saver).</summary>
public sealed partial class NetworkInfoDemo(INetworkInfo network) : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        UiCard.Class("shadow-sm")[
                UiButton.Tone(UiTone.Primary).Variant(UiVariant.Outline).Class("mb-2")
                    .Id("net-read")
                    .OnClick(Read)["Read network status"],
                Div.Class("text-sm text-ui-muted")["Connection: ", Code.Id("net-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("net-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            if (!await network.IsSupportedAsync())
            {
                _value = "not supported (try a Chromium browser)";
                _status = "Network Information unavailable";
                return;
            }

            var status = await network.GetStatusAsync();
            _value = status is null
                ? "unavailable"
                : $"{status.EffectiveType}, {status.Downlink} Mbps, {status.Rtt} ms RTT, saveData: {status.SaveData}";
            _status = "Network read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
