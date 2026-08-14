using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Rask.Example.Shared.Features;

// Inject services like HttpClient/Navigator/RouteState through the primary
// constructor — never as a public settable property. A non-nullable settable
// property would become a *required* factory parameter the caller has to pass,
// and the `required` keyword on a property + a DI-only constructor (no
// parameterless ctor) is the RASK002 warning.
public sealed partial class WeatherCard(HttpClient http) : Component
{
    private Forecast? _forecast;

    // Only the public settable properties feed the generated factory, so the
    // call site is Generated.WeatherCard(City: "Helsinki") — `http` resolves
    // from DI via ActivatorUtilities, invisible to the caller. City is a
    // non-nullable, no-initializer property, so the generator emits it as a
    // *required* factory parameter (RASK001) — note there's no `required`
    // keyword: that keyword plus a DI-only constructor (no parameterless ctor)
    // would be RASK002, since ActivatorUtilities can't satisfy `required`
    // members. Rask assigns City
    // after construction, which the CS8618 suppression acknowledges.
#pragma warning disable CS8618
    public string City { get; set; }
#pragma warning restore CS8618

    protected override async Task OnMountAsync() =>
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

// Call site is unchanged — ActivatorUtilities resolves `http`:
public sealed partial class ComponentsDiDemo : Component
{
    protected override Component? Render() => WeatherCard.City("Helsinki");
}

[JsonSerializable(typeof(WeatherCard.Forecast))]
internal sealed partial class WeatherJsonContext : JsonSerializerContext;
