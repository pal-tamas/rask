using System.Text.RegularExpressions;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// The .NET project the scaffolder is generating into: its directory and root namespace. Given a target
/// directory it derives the folder-based namespace a generated file should declare, matching the C#
/// convention (root namespace + the folder path relative to the project).
/// </summary>
internal sealed partial class ProjectContext(string projectDirectory, string rootNamespace)
{
    public string ProjectDirectory { get; } = projectDirectory;

    public string RootNamespace { get; } = rootNamespace;

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
    public static ProjectContext? Locate(IFileSystem fileSystem, string startDirectory)
    {
        var directory = Path.GetFullPath(startDirectory);

        while (!string.IsNullOrEmpty(directory))
        {
            var projects = fileSystem.ListFiles(directory, "*.csproj");
            if (projects.Count == 1)
            {
                return new ProjectContext(directory, ProjectContext.ReadRootNamespace(fileSystem, projects[0]));
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
