namespace Rask.Cli.Scaffolding;

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
            if (string.Equals(parent, directory, StringComparison.Ordinal))
            {
                break;
            }

            directory = parent!;
        }

        return $"Couldn't find a .csproj at or above '{startDirectory}'.";
    }

    /// <summary>
    /// The one <c>.csproj</c> a <c>--project</c> value names — a project file, or a directory holding exactly one —
    /// or <c>null</c> when it can't be pinned down, as <see cref="Locate"/> treats ambiguity.
    /// </summary>
    public static string? ResolveCsproj(IFileSystem fileSystem, string projectPathOrDirectory)
    {
        if (projectPathOrDirectory.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return fileSystem.FileExists(projectPathOrDirectory) ? projectPathOrDirectory : null;
        }

        try
        {
            var projects = fileSystem.ListFiles(projectPathOrDirectory, "*.csproj");
            return projects.Count == 1 ? projects[0] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ProjectContext? Locate(IFileSystem fileSystem, string startDirectory)
    {
        var directory = Path.GetFullPath(startDirectory);

        while (!string.IsNullOrEmpty(directory))
        {
            var projects = fileSystem.ListFiles(directory, "*.csproj");
            if (projects.Count == 1)
            {
                // The project file is the source of truth for whether this is a browser app.
                var csproj = fileSystem.ReadAllText(projects[0]);
                return new ProjectContext(
                    directory,
                    ProjectContext.ReadRootNamespace(fileSystem, projects[0]),
                    ProjectContext.DetectBrowser(csproj));
            }

            if (projects.Count > 1)
            {
                // Ambiguous — an explicit project directory is needed; the caller reports this.
                return null;
            }

            var parent = Path.GetDirectoryName(directory);
            if (string.Equals(parent, directory, StringComparison.Ordinal))
            {
                break;
            }

            directory = parent!;
        }

        return null;
    }
}
