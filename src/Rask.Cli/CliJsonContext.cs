using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InfoReport))]
[JsonSerializable(typeof(DeployStatusReport))]
[JsonSerializable(typeof(MigrationListReport))]
[JsonSerializable(typeof(EfMigration[]))]
[JsonSerializable(typeof(DoctorReport))]
[JsonSerializable(typeof(NewDryRunReport))]
[JsonSerializable(typeof(DevDryRunReport))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
