using System.Text.Json;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// Finds the SQLite file an app actually writes to, by reading the same setting the app reads:
/// <c>Rask:ConnectionStrings:App</c>, falling back to the <c>Data Source=app.db</c> a scaffolded
/// <c>appsettings.json</c> starts with.
/// </summary>
/// <remarks>
/// Environment-specific files win over the base one, mirroring configuration's own precedence — a
/// <c>Development</c> override is what you have locally, and it is the database you mean when you ask for
/// a backup. Nothing here parses a full connection string: only the <c>Data Source</c> keyword matters,
/// and anything else (an in-memory or shared-cache source) is reported as unsupported rather than guessed
/// at, because backing up the wrong file quietly is worse than refusing.
/// </remarks>
internal static class SqliteDatabaseLocator
{
    /// <summary>The value a scaffolded app starts with, so an app with no configured string still works.</summary>
    internal const string DefaultDataSource = "app.db";

    // The settings files are JSONC — a scaffolded appsettings.json carries comments — and .NET's own JSON
    // configuration provider reads them with exactly these options. Parsing them strictly failed on every
    // scaffolded file and quietly fell back to app.db, whatever the file said.
    private static readonly JsonDocumentOptions SettingsOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Resolve the database path for the app rooted at <paramref name="projectDirectory"/>, or explain why
    /// it cannot be resolved.
    /// </summary>
    internal static (string? Path, string? Error) Locate(
        IFileSystem fileSystem,
        string projectDirectory,
        string? environment = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(projectDirectory);

        var dataSource = ReadDataSource(fileSystem, projectDirectory, environment) ?? DefaultDataSource;

        if (dataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase) ||
            dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return (null,
                $"The connection string's data source ('{dataSource}') isn't a plain file, so there is " +
                "nothing to copy. Pass an explicit path if you meant a different database.");
        }

        // A relative source is relative to the app's content root, which is the project directory.
        var path = Path.IsPathRooted(dataSource)
            ? dataSource
            : Path.GetFullPath(Path.Combine(projectDirectory, dataSource));

        return (path, null);
    }

    /// <summary>Extract the <c>Data Source</c> value from a SQLite connection string, or null.</summary>
    internal static string? DataSourceOf(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator].Trim();

            // Microsoft.Data.Sqlite accepts all three spellings for the same keyword.
            if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("DataSource", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Filename", StringComparison.OrdinalIgnoreCase))
            {
                var value = part[(separator + 1)..].Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    // appsettings.<Environment>.json first, then appsettings.json — configuration's own precedence.
    private static string? ReadDataSource(IFileSystem fileSystem, string projectDirectory, string? environment)
    {
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
            if (!fileSystem.FileExists(path))
            {
                continue;
            }

            var connectionString = ReadAppConnectionString(fileSystem, path);
            if (connectionString is null)
            {
                continue;
            }

            var dataSource = DataSourceOf(connectionString);
            if (dataSource is not null)
            {
                return dataSource;
            }
        }

        return null;
    }

    // Rask:ConnectionStrings:App. A top-level ConnectionStrings:App is not read — the app itself no longer reads it,
    // so a backup taken from it would be of a database the app is not using.
    private static string? ReadAppConnectionString(IFileSystem fileSystem, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(fileSystem.ReadAllText(path), SettingsOptions);

            // Configuration keys are case-insensitive, so the settings file may spell them any way round.
            return Property(document.RootElement, "Rask") is { } rask
                   && Property(rask, "ConnectionStrings") is { } strings
                   && Property(strings, "App") is { ValueKind: JsonValueKind.String } app
                ? app.GetString()
                : null;
        }
        catch (JsonException)
        {
            // A hand-edited settings file shouldn't wedge a backup — fall through to the next candidate.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

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
