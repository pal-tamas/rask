using System.Net.Http.Json;

namespace Rask.Example.Components;

public sealed class HttpWeatherForecastService(HttpClient http) : IWeatherForecastService
{
    public async Task<WeatherForecast[]> GetForecastsAsync() =>
        await http.GetFromJsonAsync<WeatherForecast[]>("api/weatherforecast") ?? [];
}
