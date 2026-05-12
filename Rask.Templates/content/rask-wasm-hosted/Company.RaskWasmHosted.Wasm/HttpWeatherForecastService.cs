using System.Net.Http.Json;

namespace Company.RaskWasmHosted.Wasm;

public sealed class HttpWeatherForecastService(HttpClient http) : IWeatherForecastService
{
    public async Task<WeatherForecast[]> GetForecastsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<WeatherForecast[]>("api/weatherforecast", cancellationToken) ?? [];
}
