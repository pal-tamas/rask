using System.Text.Json.Serialization;

namespace Rask.WebPush;

[JsonSerializable(typeof(PushKey))]
[JsonSerializable(typeof(PostedSubscription))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class PushJson : JsonSerializerContext;
