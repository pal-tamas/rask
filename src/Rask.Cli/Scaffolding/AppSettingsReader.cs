using System.Text.Json;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// Reads string settings out of an app's <c>appsettings*.json</c> files, in configuration's own precedence, for
/// commands that must act on what the app itself would read without running it.
/// </summary>
internal static class AppSettingsReader
{
    // The settings files are JSONC — a scaffolded appsettings.json carries comments — and .NET's own JSON
    // configuration provider reads them with exactly these options. Parsing them strictly failed on every
    // scaffolded file and quietly fell back to the default, whatever the file said.
    private static readonly JsonDocumentOptions SettingsOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Every string value at <paramref name="keyPath"/>, most specific file first:
    /// <c>appsettings.&lt;environment&gt;.json</c>, then <c>appsettings.Development.json</c>, then
    /// <c>appsettings.json</c>. Files that are missing, unreadable or don't set the key are skipped.
    /// </summary>
    internal static IEnumerable<string> ReadStrings(
        IFileSystem fileSystem,
        string projectDirectory,
        string? environment,
        params string[] keyPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(projectDirectory);

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(environment))
        {
            candidates.Add($"appsettings.{environment}.json");
        }

        candidates.Add("appsettings.Development.json");
        candidates.Add("appsettings.json");

        foreach (var candidate in candidates)
        {
            var path = Path.Combine(projectDirectory, candidate);
            if (fileSystem.FileExists(path) && ReadString(fileSystem, path, keyPath) is { } value)
            {
                yield return value;
            }
        }
    }

    private static string? ReadString(IFileSystem fileSystem, string path, string[] keyPath)
    {
        try
        {
            using var document = JsonDocument.Parse(fileSystem.ReadAllText(path), SettingsOptions);
            JsonElement? element = document.RootElement;
            foreach (var key in keyPath)
            {
                element = element is { } current ? Property(current, key) : null;
            }

            return element is { ValueKind: JsonValueKind.String } found ? found.GetString() : null;
        }
        catch (JsonException)
        {
            // A hand-edited settings file shouldn't wedge a command — fall through to the next candidate.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // Configuration keys are case-insensitive, so the settings file may spell them any way round.
    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
