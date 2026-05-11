namespace Rask.Example.Components;

public sealed class LocalWeatherForecastService : IWeatherForecastService
{
    private static readonly string[] Summaries =
        ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

    public async Task<WeatherForecast[]> GetForecastsAsync()
    {
        await Task.Delay(500);
        var startDate = DateOnly.FromDateTime(DateTime.Now);
        var rng = Random.Shared;
        return Enumerable.Range(1, 5).Select(i => new WeatherForecast(
            startDate.AddDays(i),
            rng.Next(-20, 55),
            Summaries[rng.Next(Summaries.Length)]
        )).ToArray();
    }
}
