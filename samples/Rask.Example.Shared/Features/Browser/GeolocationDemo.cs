using System.Globalization;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IGeolocation" /> — one-shot current position via the Promise-wrapped helper.</summary>
public sealed partial class GeolocationDemo(IGeolocation geolocation) : Component
{
    private string? _location;
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Ui.Card} shadow-sm border-0")[
            Div.Class(Ui.CardBody)[
                Button.Class($"{Ui.BtnOutlinePrimary} mb-2").Type("button")
                    .Id("geo-get")
                    .OnClickAsync(Get)[
                    "Get current position"],
                Div.Class("small text-secondary")["Position: ", Code.Id("geo-value")[_location ?? "(not requested)"]],
                Div.Class("small text-secondary")["Status: ", Code.Id("geo-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task Get()
    {
        try
        {
            var pos = await geolocation.GetCurrentPositionAsync(new GeolocationOptions { TimeoutMs = 10_000 });
            // Coordinates format invariantly (decimal point) — independent of the server's locale.
            _location = string.Create(
                CultureInfo.InvariantCulture,
                $"lat {pos.Latitude:F4}, lon {pos.Longitude:F4} (±{pos.Accuracy:F0} m)");
            _status = "Position acquired";
        }
        catch (Exception ex)
        {
            _location = null;
            _status = "Location failed: " + ex.Message;
        }
    }
}
