using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>
///     The <c>--json</c> surface: the machine-readable form of the commands worth scripting.
/// </summary>
/// <remarks>
///     <para>
///         Declared in one place so the three commands that offer it cannot drift on the flag's spelling,
///         its help text, or its serialization settings — the same reasoning as the shared argument
///         schema. <see cref="Flag" /> is what a command adds; <see cref="Write" /> is how it emits.
///     </para>
///     <para>
///         Source-generated contexts, matching the rest of the CLI (<c>DeployConfig</c>,
///         <c>GenerateConfig</c>): reflection-based serialization would be a trimming hazard in a tool
///         that ships as a self-contained binary, and the shapes here are fixed and few.
///     </para>
///     <para>
///         Output goes to <see cref="IConsole.Out" /> unstyled and unindented-by-nothing-else: a
///         <c>--json</c> run prints the document and nothing else, so <c>rask info --json | jq</c> works
///         without filtering banners out. Errors still go to stderr, so a failed run is distinguishable
///         by exit code and stream rather than by parsing.
///     </para>
/// </remarks>
internal static class JsonOutput
{
    /// <summary>The flag every <c>--json</c>-capable command declares, worded once.</summary>
    public static ArgumentSchema WithJson(this ArgumentSchema schema) =>
        schema.Flag("json", description: "Print the result as JSON instead of a human-readable report.");

    public static void Write<T>(IConsole console, T value, JsonTypeInfo<T> typeInfo) =>
        console.Out.WriteLine(JsonSerializer.Serialize(value, typeInfo));
}

/// <summary>The <c>rask info --json</c> document.</summary>
internal sealed record InfoReport(
    [property: JsonPropertyName("raskCli")] string RaskCli,
    [property: JsonPropertyName("dotnetSdk")] string? DotnetSdk,
    [property: JsonPropertyName("os")] string Os);

/// <summary>
///     One row of <c>rask deploy status --json</c>. Named for the report rather than the thing, because
///     <c>DeployedApp</c> already exists for the Caddy routing path and means something narrower.
/// </summary>
internal sealed record DeployedAppStatus(
    string App,
    string Container,
    string? Domain,
    string? Ports,
    string? Color,
    string Status,
    bool IsCurrentProject);

/// <summary>The <c>rask deploy status --json</c> document.</summary>
internal sealed record DeployStatusReport(string Host, IReadOnlyList<DeployedAppStatus> Apps);

/// <summary>One migration in <c>rask db list --json</c>.</summary>
internal sealed record MigrationEntry(string Id, string Name, bool Applied);

/// <summary>The <c>rask db list --json</c> document.</summary>
internal sealed record MigrationListReport(IReadOnlyList<MigrationEntry> Migrations);

/// <summary>
///     One entry of <c>dotnet ef migrations list --json</c>, which is what <c>rask db list --json</c> is
///     built from rather than from the human listing.
/// </summary>
/// <remarks>
///     EF's shape, not ours, and deliberately kept separate from <see cref="MigrationEntry" /> so their
///     fields can diverge without one silently reshaping the other. <c>safeName</c> is EF's
///     identifier-safe variant and is dropped — it exists for code generation, not for a report.
/// </remarks>
internal sealed record EfMigration(string Id, string Name, string? SafeName, bool Applied);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InfoReport))]
[JsonSerializable(typeof(DeployStatusReport))]
[JsonSerializable(typeof(MigrationListReport))]
[JsonSerializable(typeof(EfMigration[]))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
