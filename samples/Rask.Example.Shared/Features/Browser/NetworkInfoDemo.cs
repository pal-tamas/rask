using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="INetworkInfo" /> — read the connection quality (effective type, downlink, Data Saver).</summary>
public sealed class NetworkInfoDemo(INetworkInfo network) : Component
{
    private string? _value;
    private string? _status;

    protected override RenderResult Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                Button(Class: "btn btn-outline-primary btn-sm mb-2", Id: "net-read", OnClickAsync: Read)[
                    "Read network status"],
                Div(Class: "small text-secondary")["Connection: ", Code(Id: "net-value")[_value ?? "(not requested)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "net-status")[_status ?? "(idle)"]]
            ]
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
