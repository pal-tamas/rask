using System.Globalization;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IGeolocation.WatchAsync" /> — live position tracking. Start watching and the browser
///     pushes each fix to C#, which re-renders the readout (the handler calls <c>StateHasChanged()</c>,
///     the sanctioned pushed-update pattern). Stop disposes the watch (<c>clearWatch</c>).
/// </summary>
public sealed class GeolocationWatchDemo(IGeolocation geolocation) : Component, IAsyncDisposable
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private IAsyncDisposable? _watch;
    private string? _location;
    private int _fixes;
    private string? _status;

    protected override Component? Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    _watch is null
                        ? Button(Class: "btn btn-primary btn-sm", Id: "geowatch-start", OnClickAsync: Start)[
                            "Start watching"]
                        : Button(Class: "btn btn-outline-danger btn-sm", Id: "geowatch-stop", OnClickAsync: Stop)[
                            "Stop"]
                ],
                Div(Class: "small text-secondary")[
                    "Position: ", Code(Id: "geowatch-value")[_location ?? "(not watching)"],
                    Span(Class: "ms-2", Id: "geowatch-fixes")[$"({_fixes} fix(es))"]
                ],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "geowatch-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task Start()
    {
        try
        {
            _watch = await geolocation.WatchAsync(pos =>
            {
                _fixes++;
                _location = string.Create(Inv, $"lat {pos.Latitude:F4}, lon {pos.Longitude:F4} (±{pos.Accuracy:F0} m)");
                StateHasChanged();
                return Task.CompletedTask;
            }, new GeolocationOptions { EnableHighAccuracy = true });
            _status = "Watching — move the device to see updates";
        }
        catch (Exception ex)
        {
            _status = "Watch failed: " + ex.Message;
        }
    }

    private async Task Stop()
    {
        if (_watch is not null)
        {
            await _watch.DisposeAsync();
            _watch = null;
        }

        _status = "Stopped";
    }

    public async ValueTask DisposeAsync()
    {
        if (_watch is not null)
        {
            await _watch.DisposeAsync();
        }
    }
}
