
namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>watchPosition</c> from Rask.Web — live position tracking. The browser hands each fix to the
///     handler, which re-renders this; Stop calls <c>clearWatch</c>.
/// </summary>
public sealed partial class GeolocationWatchDemo : Component
{
    private int? _watchId;
    private string? _location;
    private int _fixes;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    _watchId is null
                        ? Ui.Button.Primary.Id("geowatch-start").OnClick(Start)["Start watching"]
                        : Ui.Button.Error.Outline.Id("geowatch-stop").OnClick(Stop)["Stop"]
                ],
                Div.Class("text-sm text-ui-muted")[
                    "Position: ", Code.Id("geowatch-value")[_location ?? "(not watching)"],
                    Span.Class("ms-2").Id("geowatch-fixes")[$"({_fixes} fix(es))"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("geowatch-status")[_status ?? "(idle)"]]
            ];

    private async Task Start()
    {
        try
        {
            _watchId = await Navigator.Geolocation.WatchPosition(
                p =>
                {
                    _fixes++;
                    _location = GeolocationDemo.Describe(p.Coords);
                },
                e => _status = "Watch failed: " + e.Message,
                new Types.PositionOptions { EnableHighAccuracy = true });
            _status = "Watching — move the device to see updates";
        }
        catch (Exception ex) { _status = "Watch failed: " + ex.Message; }
    }

    private async Task Stop()
    {
        await ClearWatch();
        _status = "Stopped";
    }

    protected override Task OnUnmount() => ClearWatch();

    private async Task ClearWatch()
    {
        if (_watchId is { } id)
        {
            _watchId = null;
            await Navigator.Geolocation.ClearWatch(id);
        }
    }
}
