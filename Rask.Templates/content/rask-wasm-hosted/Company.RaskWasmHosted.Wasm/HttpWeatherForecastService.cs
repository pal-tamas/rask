using System.Net.Http.Json;

namespace Company.RaskWasmHosted.Wasm;

public sealed class HttpWeatherForecastService(HttpClient http) : IWeatherForecastService
{
    public async Task<WeatherForecast[]> GetForecastsAsync() =>
        await http.GetFromJsonAsync<WeatherForecast[]>("api/weatherforecast") ?? [];
}
