using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Cli.Scaffolding;

/// <summary>Source-generated (reflection-free) serialization for <see cref="DeployConfig"/>.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DeployConfig))]
internal sealed partial class DeployConfigJsonContext : JsonSerializerContext;
