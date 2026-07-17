using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// Team defaults for <c>rask generate feature</c>, persisted at <c>.rask/generate.json</c> in the project
/// so everyone scaffolds the same shape without re-typing flags. Explicit command-line flags always win;
/// these only fill in what you didn't pass. Booleans are opt-in (a <c>null</c>/absent value means "off"),
/// and <c>--save-defaults</c> writes the feature flags from the current invocation back here.
/// </summary>
internal sealed class GenerateConfig
{
    public bool? Bs { get; set; }

    public bool? Modal { get; set; }

    public bool? SoftDelete { get; set; }

    public bool? Concurrency { get; set; }

    public bool? Events { get; set; }

    public bool? Outbox { get; set; }

    public bool? Tests { get; set; }

    public string? Validation { get; set; }

    public string? Id { get; set; }

    /// <summary>The <c>.rask/generate.json</c> path under <paramref name="projectDirectory"/>.</summary>
    public static string PathFor(string projectDirectory) =>
        Path.Combine(projectDirectory, ".rask", "generate.json");

    /// <summary>Load the persisted defaults, or an empty set when the file is absent or unreadable.</summary>
    public static GenerateConfig Load(IFileSystem fileSystem, string projectDirectory)
    {
        var path = PathFor(projectDirectory);
        if (!fileSystem.FileExists(path))
        {
            return new GenerateConfig();
        }

        try
        {
            return JsonSerializer.Deserialize(fileSystem.ReadAllText(path), GenerateConfigJsonContext.Default.GenerateConfig)
                ?? new GenerateConfig();
        }
        catch (JsonException)
        {
            // A hand-edited or corrupt file shouldn't wedge a scaffold — fall back to flags/built-in defaults.
            return new GenerateConfig();
        }
    }

    /// <summary>Write the defaults to <c>.rask/generate.json</c> (creating the <c>.rask</c> directory).</summary>
    public void Save(IFileSystem fileSystem, string projectDirectory)
    {
        var path = PathFor(projectDirectory);
        fileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
        fileSystem.WriteAllText(path, JsonSerializer.Serialize(this, GenerateConfigJsonContext.Default.GenerateConfig));
    }
}

/// <summary>Source-generated (reflection-free) serialization for <see cref="GenerateConfig"/>.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GenerateConfig))]
internal sealed partial class GenerateConfigJsonContext : JsonSerializerContext;
