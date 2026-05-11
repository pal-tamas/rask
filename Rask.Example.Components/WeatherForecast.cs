namespace Rask.Example.Components;

public sealed record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public interface IWeatherForecastService
{
    Task<WeatherForecast[]> GetForecastsAsync();
}
