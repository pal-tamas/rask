using Rask.Core.Routing;

namespace Company.RaskWasmHosted.Wasm;

[Route("/weather")]
public sealed class Weather(IWeatherForecastService service) : Component
{
    private WeatherForecast[]? _forecasts;

    protected override async Task OnMountAsync() =>
        _forecasts = await service.GetForecastsAsync(CancellationToken);

    protected override Component? Render() =>
        [
            H1()["Weather"],
            P()["Fetched over HTTP from the ASP.NET host."],
            _forecasts is null
                ? P()[Em()["Loading..."]]
                : Table()[
                    Thead()[
                        Tr()[
                            Th()["Date"],
                            Th()["Temp. (C)"],
                            Th()["Temp. (F)"],
                            Th()["Summary"]
                        ]
                    ],
                    Tbody()[_forecasts.Select(f => Tr(Key: f.Date)[
                        Td()[f.Date.ToString("yyyy-MM-dd")],
                        Td()[f.TemperatureC],
                        Td()[f.TemperatureF],
                        Td()[f.Summary ?? ""]
                    ]).ToArray()]
                ]
        ];
}
