using System.Text.RegularExpressions;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// The repository's CI workflow, which the deploy workflow waits on (<c>workflow_run</c>) so only a commit CI passed
/// goes live. Found among <c>.github/workflows/*.yml</c>: <c>ci.yml</c> when there is one, else the only workflow that
/// runs on push. GitHub names a workflow by its <c>name:</c>, or by its path when it has none — and that name is what
/// <c>workflow_run</c> matches.
/// </summary>
internal static partial class CiWorkflow
{
    /// <summary>The name of the CI workflow under <paramref name="directory"/>, or <c>null</c> when there is no single one.</summary>
    public static string? Find(IFileSystem fileSystem, string directory)
    {
        var folder = Path.Combine(directory, ".github", "workflows");
        if (!fileSystem.DirectoryExists(folder))
        {
            return null;
        }

        var candidates = fileSystem.ListFiles(folder, "*.yml")
            .Concat(fileSystem.ListFiles(folder, "*.yaml"))
            .Where(path => !string.Equals(Path.GetFileName(path), Path.GetFileName(GitHubActionsWorkflow.RelativePath), StringComparison.OrdinalIgnoreCase))
            .Select(path => (Path: path, Text: fileSystem.ReadAllText(path)))
            .Where(file => OnPush().IsMatch(file.Text))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToList();

        var chosen = candidates.FirstOrDefault(file => Path.GetFileNameWithoutExtension(file.Path).Equals("ci", StringComparison.OrdinalIgnoreCase));
        if (chosen.Path is null)
        {
            if (candidates.Count != 1)
            {
                return null;
            }

            chosen = candidates[0];
        }

        return NameOf(chosen.Text) ?? $".github/workflows/{Path.GetFileName(chosen.Path)}";
    }

    // The top-level `name:`, unquoted. An expression (`${{ }}`) is not a name workflow_run can match.
    private static string? NameOf(string workflow)
    {
        var match = TopLevelName().Match(workflow);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim();
        if (name.Length >= 2 && (name[0] == '"' || name[0] == '\'') && name[^1] == name[0])
        {
            name = name[1..^1];
        }

        return name.Length == 0 || name.Contains("${{", StringComparison.Ordinal) ? null : name;
    }

    [GeneratedRegex(@"^name:[ \t]*(?<name>[^#\r\n]+)", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TopLevelName();

    // `on: push`, `on: [push, …]`, or a `push:` key under `on:` — a workflow that runs when main moves.
    [GeneratedRegex(@"^\s*(?:-\s*)?push\s*:|^on:.*\bpush\b", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OnPush();
}
