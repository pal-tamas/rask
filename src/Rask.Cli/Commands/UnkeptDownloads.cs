namespace Rask.Cli.Commands;

/// <summary>
/// The downloads a .NET image build makes that <see cref="RunnerBuildCache"/> keeps between deploys only when the
/// Dockerfile holds them in a <c>RUN --mount=type=cache</c>: anything outside a mount lives in a layer after the
/// app's sources, rebuilt on every commit, so it is downloaded again on every deploy. The template Dockerfiles mount
/// them all; a hand-written one easily misses one, and nothing else would ever say so.
/// </summary>
internal static class UnkeptDownloads
{
    /// <summary>What a Rask app's publish downloads, by the folder it keeps it in inside the SDK image (as root).</summary>
    private static readonly (string What, string Folder, string Override)[] Downloads =
    [
        ("NuGet's packages", "/root/.nuget/packages", "NUGET_PACKAGES"),
        ("the Tailwind CLI", "/root/.rask/tailwind", "RaskTailwindCacheRoot"),
        ("tsgo", "/root/.rask/typescript", "RaskTypeScriptCacheRoot"),
    ];

    /// <summary>
    /// Each download <paramref name="dockerfile"/>'s <c>dotnet publish</c> makes that no cache mount holds, as
    /// "what (folder)"; none for a Dockerfile that publishes no .NET app, or one that moves the folder elsewhere.
    /// </summary>
    internal static IReadOnlyList<string> In(string dockerfile)
    {
        if (!dockerfile.Contains("dotnet publish", StringComparison.Ordinal))
        {
            return [];
        }

        var targets = RunnerBuildCache.CacheMountTargets(dockerfile);
        return
        [
            .. Downloads
                .Where(download => !targets.Contains(download.Folder, StringComparer.Ordinal)
                    && !dockerfile.Contains(download.Override, StringComparison.Ordinal))
                .Select(download => $"{download.What} ({download.Folder})"),
        ];
    }
}
