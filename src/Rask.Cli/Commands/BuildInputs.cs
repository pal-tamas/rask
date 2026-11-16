using System.Security.Cryptography;
using System.Text;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// What decides what an image build downloads: the Dockerfile and every file NuGet's restore reads in the build
/// context. Two builds with the same <see cref="Fingerprint"/> need the same packages, Tailwind CLI and PDF shell, so a
/// runner cache saved for one already holds all the other needs (<see cref="RunnerBuildCache"/>).
/// </summary>
/// <remarks>
/// The same files, by name, as the deploy workflow's cache key hashes (<c>GitHubActionsWorkflow</c>), but only those
/// inside the context: one outside it cannot change the build, so a key it moved still finds this fingerprint unchanged
/// and the cache is saved again exactly as it was restored.
/// </remarks>
internal static class BuildInputs
{
    /// <summary>Bumped when what the cache holds changes shape, so an old saved cache is written again.</summary>
    private const string Format = "rask-build-cache/1";

    private static readonly string[] RestoreFiles = ["global.json", "nuget.config"];

    /// <summary>A SHA-256, in hex, over the Dockerfile and the restore inputs under <paramref name="contextDir"/>.</summary>
    internal static string Fingerprint(IFileSystem fileSystem, string contextDir, string dockerfile)
    {
        var text = new StringBuilder(Format).Append('\n').Append(dockerfile).Append('\n');
        var inputs = fileSystem.ListFilesRecursive(contextDir, "*")
            .Where(IsRestoreInput)
            .Select(path => Path.GetRelativePath(contextDir, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        foreach (var path in inputs)
        {
            text.Append(path).Append('\n').Append(fileSystem.ReadAllText(Path.Combine(contextDir, path))).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static bool IsRestoreInput(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || (name.StartsWith("Directory.", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
            || RestoreFiles.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
