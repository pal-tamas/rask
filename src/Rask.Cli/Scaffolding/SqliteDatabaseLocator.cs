using System.Text.Json;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// Finds the SQLite file an app actually writes to, by reading the same setting the app reads:
/// <c>ConnectionStrings:App</c>, with the same <c>Data Source=app.db</c> fallback the scaffolded
/// <c>Program.cs</c> uses.
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
    /// <summary>The value the scaffolder falls back to, so an app with no configured string still works.</summary>
    internal const string DefaultDataSource = "app.db";

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

    private static string? ReadAppConnectionString(IFileSystem fileSystem, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(fileSystem.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("ConnectionStrings", out var strings) ||
                strings.ValueKind != JsonValueKind.Object ||
                !strings.TryGetProperty("App", out var app) ||
                app.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return app.GetString();
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
}
