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

    // appsettings.<Environment>.json first, then appsettings.json — configuration's own precedence. A file whose
    // string names no data source falls through to the next. A top-level ConnectionStrings:App is not read — the
    // app itself no longer reads it, so a backup taken from it would be of a database the app is not using.
    private static string? ReadDataSource(IFileSystem fileSystem, string projectDirectory, string? environment) =>
        AppSettingsReader.ReadStrings(fileSystem, projectDirectory, environment, "Rask", "ConnectionStrings", "App")
            .Select(DataSourceOf)
            .FirstOrDefault(source => source is not null);
}
