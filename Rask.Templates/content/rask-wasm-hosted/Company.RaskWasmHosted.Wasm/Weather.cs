using Rask.Core.Routing;

namespace Company.RaskWasmHosted.Wasm;

[Route("/weather")]
public sealed class Weather(IWeatherForecastService service) : Component
{
    private WeatherForecast[]? _forecasts;

    protected override async Task OnInitializedAsync() =>
        _forecasts = await service.GetForecastsAsync();

    public override Component Render() =>
        Fragment(
            H1(Children: ["Weather"]),
            P(Children: ["Fetched over HTTP from the ASP.NET host."]),
            _forecasts is null
                ? P(Children: [Em(Children: ["Loading..."])])
                : Table(Children:
                [
                    Thead(Children:
                    [
                        Tr(Children:
                        [
                            Th(Children: ["Date"]),
                            Th(Children: ["Temp. (C)"]),
                            Th(Children: ["Temp. (F)"]),
                            Th(Children: ["Summary"])
                        ])
                    ]),
                    Tbody(Children: _forecasts.Select(f => (Child)Tr(Children:
                    [
                        Td(Children: [f.Date.ToString("yyyy-MM-dd")]),
                        Td(Children: [f.TemperatureC.ToString()]),
                        Td(Children: [f.TemperatureF.ToString()]),
                        Td(Children: [f.Summary ?? ""])
                    ])).ToArray())
                ])
        );
}
