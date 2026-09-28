using System.Text.Json.Serialization;

namespace Rask.Site.Features;

[JsonSerializable(typeof(HttpFetchDemo.Post))]
internal sealed partial class HttpJsonContext : JsonSerializerContext;
