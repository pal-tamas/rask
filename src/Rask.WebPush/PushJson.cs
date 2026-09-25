using System.Text.Json.Serialization;
using Rask.Wire;

namespace Rask.WebPush;

[JsonSerializable(typeof(PushKey))]
[JsonSerializable(typeof(PushSubscription))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class PushJson : JsonSerializerContext;
