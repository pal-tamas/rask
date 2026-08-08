using System.Text.RegularExpressions;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// The .NET project the scaffolder is generating into: its directory and root namespace. Given a target
/// directory it derives the folder-based namespace a generated file should declare, matching the C#
/// convention (root namespace + the folder path relative to the project).
/// </summary>
internal sealed partial class ProjectContext(
    string projectDirectory,
    string rootNamespace,
    DatabaseProvider provider = DatabaseProvider.Sqlite,
    bool isBrowser = false)
{
    public string ProjectDirectory { get; } = projectDirectory;

    public string RootNamespace { get; } = rootNamespace;

    /// <summary>
    /// Whether this project runs in the browser (a WASM app), read off its project file the same way
    /// <see cref="Provider"/> is. Scaffolding that depends on a host-side facility has to know: the
    /// browser has no hosted services, no design-time database and no durable file by default, so a
    /// generator that assumes a server would emit something that compiles and then quietly does nothing.
    /// </summary>
    public bool IsBrowser { get; } = isBrowser;

    /// <summary>
    /// The database this project is wired to, read off its package references rather than asked for again.
    /// </summary>
    /// <remarks>
    /// Detected, not configured: the provider was already decided by <c>rask new --database</c>, and a
    /// second source of truth is a second thing to get out of sync — a <c>rask generate feature</c> that
    /// emitted SQLite wiring into a PostgreSQL app would not fail until runtime.
    /// </remarks>
    public DatabaseProvider Provider { get; } = provider;

    /// <summary>The scaffolding facts for <see cref="Provider"/>.</summary>
    public DatabaseInfo Database => DatabaseCatalog.For(Provider);

    /// <summary>The namespace a file in <paramref name="targetDirectory"/> should declare.</summary>
    public string NamespaceFor(string targetDirectory)
    {
        var relative = Path.GetRelativePath(ProjectDirectory, targetDirectory);
        if (relative is "." or "")
        {
            return RootNamespace;
        }

        var parts = new List<string> { RootNamespace };
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            // A target outside the project (a leading "..") can't map to a child namespace — fall back
            // to the root namespace rather than emitting something invalid.
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                return RootNamespace;
            }

            if (Identifiers.ToNamespacePart(segment) is { } part)
            {
                parts.Add(part);
            }
        }

        return string.Join('.', parts);
    }

    [GeneratedRegex(@"<RootNamespace>\s*(.+?)\s*</RootNamespace>", RegexOptions.IgnoreCase)]
    private static partial Regex RootNamespaceRegex();

    // A browser TFM on either the singular or plural element — net10.0-browser today, but matched by the
    // "-browser" suffix rather than the version so a framework bump doesn't silently stop detecting it.
    [GeneratedRegex(@"<TargetFrameworks?>[^<]*-browser", RegexOptions.IgnoreCase)]
    private static partial Regex BrowserTargetFrameworkRegex();

    /// <summary>Whether <paramref name="csprojText"/> describes a project that runs in the browser.</summary>
    internal static bool DetectBrowser(string csprojText)
    {
        ArgumentNullException.ThrowIfNull(csprojText);

        // Any one of the three is conclusive, and a hand-edited project may carry only one of them.
        // The package check keeps its closing quote so it does NOT match Rask.Wasm.Hosting — that is
        // referenced by the SERVER half of a hosted-WASM solution, which is the opposite answer.
        return BrowserTargetFrameworkRegex().IsMatch(csprojText)
            || csprojText.Contains("<RaskWasm>true</RaskWasm>", StringComparison.OrdinalIgnoreCase)
            || csprojText.Contains("Include=\"Rask.Wasm\"", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ReadRootNamespace(IFileSystem fileSystem, string csprojPath)
    {
        // An explicit <RootNamespace> wins; otherwise the SDK default is the project file name. Either
        // way the value is sanitized into a valid namespace — an explicit "1Store" or "My-App" must not
        // reach a generated file verbatim (it wouldn't compile).
        var match = RootNamespaceRegex().Match(fileSystem.ReadAllText(csprojPath));
        var raw = match.Success ? match.Groups[1].Value : Path.GetFileNameWithoutExtension(csprojPath);
        return SanitizeNamespace(raw);
    }

    private static string SanitizeNamespace(string raw)
    {
        var parts = raw
            .Split('.')
            .Select(Identifiers.ToNamespacePart)
            .Where(part => part is not null);

        var joined = string.Join('.', parts);
        return joined.Length == 0 ? "App" : joined;
    }
}

/// <summary>Locates the .NET project that owns a directory by walking up to the nearest single <c>*.csproj</c>.</summary>
internal static class ProjectLocator
{
    /// <summary>
    /// Why <see cref="Locate"/> came back empty, as a sentence to print. Worth distinguishing: the walk
    /// stops on a directory holding <em>several</em> projects just as it does on finding none, and telling
    /// someone standing in a two-project folder that nothing was found sends them looking for the wrong
    /// problem. Callers append their own way out, which differs by command.
    /// </summary>
    public static string DescribeMissing(IFileSystem fileSystem, string startDirectory)
    {
        var directory = Path.GetFullPath(startDirectory);

        while (!string.IsNullOrEmpty(directory))
        {
            if (fileSystem.ListFiles(directory, "*.csproj").Count > 1)
            {
                return $"Found more than one .csproj in '{directory}', so it's ambiguous which project to use.";
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent == directory)
            {
                break;
            }

            directory = parent!;
        }

        return $"Couldn't find a .csproj at or above '{startDirectory}'.";
    }

    public static ProjectContext? Locate(IFileSystem fileSystem, string startDirectory)
    {
        var directory = Path.GetFullPath(startDirectory);

        while (!string.IsNullOrEmpty(directory))
        {
            var projects = fileSystem.ListFiles(directory, "*.csproj");
            if (projects.Count == 1)
            {
                var csproj = fileSystem.ReadAllText(projects[0]);
                return new ProjectContext(
                    directory,
                    ProjectContext.ReadRootNamespace(fileSystem, projects[0]),
                    DatabaseCatalog.DetectProvider(csproj),
                    ProjectContext.DetectBrowser(csproj));
            }

            if (projects.Count > 1)
            {
                // Ambiguous — an explicit project directory is needed; the caller reports this.
                return null;
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent == directory)
            {
                break;
            }

            directory = parent!;
        }

        return null;
    }
}
