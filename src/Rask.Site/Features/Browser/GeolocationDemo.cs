using System.Globalization;

namespace Rask.Site.Features;

/// <summary>MDN's Geolocation API from Rask.Web — one-shot current position, handed to a callback.</summary>
public sealed partial class GeolocationDemo : Component
{
    private string? _location;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Ui.Button.Primary.Outline.Class("mb-2")
                    .Id("geo-get")
                    .OnClick(Get)["Get current position"],
                Div.Class("text-sm text-ui-muted")["Position: ", Code.Id("geo-value")[_location ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("geo-status")[_status ?? "(idle)"]]
            ];

    private async Task Get()
    {
        try
        {
            _status = "Locating…";
            await Navigator.Geolocation.GetCurrentPosition(
                p =>
                {
                    _location = Describe(p.Coords);
                    _status = "Position acquired";
                },
                e => Failed(e.Message),
                new Types.PositionOptions { Timeout = 10_000 });
        }
        catch (Exception ex) { Failed(ex.Message); }
    }

    private void Failed(string message)
    {
        _location = null;
        _status = "Location failed: " + message;
    }

    // Coordinates format invariantly (decimal point) — independent of the server's locale.
    internal static string Describe(Types.GeolocationCoordinates c) =>
        string.Create(CultureInfo.InvariantCulture, $"lat {c.Latitude:F4}, lon {c.Longitude:F4} (±{c.Accuracy:F0} m)");
}
