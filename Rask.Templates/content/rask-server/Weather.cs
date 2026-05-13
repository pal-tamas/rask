using Rask.Core.Routing;

namespace Company.RaskServer;

[Route("/weather")]
public sealed class Weather(IWeatherForecastService service) : Component
{
    private WeatherForecast[]? _forecasts;

    protected override async Task OnMountAsync() =>
        _forecasts = await service.GetForecastsAsync(CancellationToken);

    public override Component Render() =>
        Fragment()[
            H1()["Weather"],
            P()["This component demonstrates showing async data."],
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
                    Tbody()[_forecasts.Select(f => (Child)Tr()[
                        Td()[f.Date.ToString("yyyy-MM-dd")],
                        Td()[f.TemperatureC.ToString()],
                        Td()[f.TemperatureF.ToString()],
                        Td()[f.Summary ?? ""]
                    ]).ToArray()]
                ]
        ];
}
