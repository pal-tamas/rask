using System.Text.Json.Serialization;

namespace Rask.Site.Features;

[JsonSerializable(typeof(WeatherCard.Forecast))]
internal sealed partial class WeatherJsonContext : JsonSerializerContext;
