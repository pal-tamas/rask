using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Rask.Site.Features;

// Inject services like HttpClient/Navigator/RouteState through the primary
// constructor — never as a public settable property. A non-nullable settable
// property would become a *required* factory parameter the caller has to pass,
// and the `required` keyword on a property + a DI-only constructor (no
// parameterless ctor) is the RASK002 warning.
public sealed partial class WeatherCard(HttpClient http) : Component
{
    private Forecast? _forecast;

    // Only the public settable properties become chain steps, so the call
    // site is WeatherCard.City("Helsinki") — `http` resolves from DI,
    // invisible to the caller. City is non-nullable with no initializer, so
    // it is a *required* step (RASK001); Rask assigns it after construction,
    // which the CS8618 suppression acknowledges.
#pragma warning disable CS8618
    public string City { get; set; }
#pragma warning restore CS8618

    protected override async Task OnMount() =>
        _forecast = await http.GetFromJsonAsync(
            $"data/weather-{City.ToLowerInvariant()}.json",
            WeatherJsonContext.Default.Forecast,
            CancellationToken);

    protected override Component? Render() =>
        _forecast is null
            ? P[Em["Loading…"]]
            : Article[
                H3[City],
                P[$"{_forecast.Summary}, {_forecast.TemperatureC} °C"]
            ];

    public sealed record Forecast(
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("temperatureC")] int TemperatureC);
}
