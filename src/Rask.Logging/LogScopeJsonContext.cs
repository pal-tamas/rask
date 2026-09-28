using System.Text.Json.Serialization;

namespace Rask.Logging;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LogScopeJsonContext : JsonSerializerContext;
