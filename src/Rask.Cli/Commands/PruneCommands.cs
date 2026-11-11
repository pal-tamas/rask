using static Rask.Cli.Commands.DockerCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// What a deploy runs to give the box its disk back, and how it reads what each freed.
///
/// <para>Every deploy moves <c>:current</c> to <c>:previous</c> and builds a new <c>:current</c>, so the image that was
/// <c>:previous</c> loses its last tag and is left behind <em>dangling</em> — and holds its build cache with it. Nothing
/// removed those, so a box deploying a few times a day filled its disk in weeks.</para>
/// <list type="bullet">
/// <item><c>docker image prune -f</c> removes dangling images only. <b>Never <c>-a</c></b>: that removes every image no
/// container runs, which includes <c>:previous</c> — the image <c>rask deploy rollback</c> restores. Tagged images,
/// and any image a container uses, are never dangling, so every app's <c>:current</c>, <c>:latest</c> and
/// <c>:previous</c> survive, and so do the proxy's and the probe's.</item>
/// <item><c>docker builder prune -f --reserved-space 20GB</c> trims the build cache, least recently used first, down to
/// <see cref="BuildCacheKept"/>: what the next builds reuse stays, and the cache stays bounded however often a box
/// deploys (an age filter alone would not bound it). BuildKit never removes cache a running build is using. The flag
/// is the docker client's, not the box's: buildx renamed <c>--keep-storage</c> to <c>--reserved-space</c> (v0.17+,
/// the old name kept as a deprecated alias), and a client without buildx knows only the old name — so a client that
/// says <c>unknown flag</c> is asked again with <see cref="LegacyBuildCacheFlag"/>.</item>
/// </list>
/// </summary>
internal static class PruneCommands
{
    /// <summary>The build cache a deploy leaves on the box: room for a few apps' SDK, restore and publish layers.</summary>
    internal const string BuildCacheKept = "20GB";

    internal const string BuildCacheFlag = "--reserved-space";

    internal const string LegacyBuildCacheFlag = "--keep-storage";

    internal static IReadOnlyList<string> BuildImagePruneArguments(string host) =>
        [.. Prefix(host), "image", "prune", "-f"];

    internal static IReadOnlyList<string> BuildBuilderPruneArguments(string host, string flag = BuildCacheFlag) =>
        [.. Prefix(host), "builder", "prune", "-f", flag, BuildCacheKept];

    /// <summary>Whether a docker client refused a flag it does not know — an older one, asked with a newer flag's name.</summary>
    internal static bool IsUnknownFlag(string error) => error.Contains("unknown flag", StringComparison.Ordinal);

    /// <summary>What docker says it could free, per kind: <c>Images\t98.2GB (38%)</c>, <c>Build Cache\t8.1GB</c>, …</summary>
    internal static IReadOnlyList<string> BuildSystemDfArguments(string host) =>
        [.. Prefix(host), "system", "df", "--format", "{{.Type}}\t{{.Reclaimable}}"];

    /// <summary>
    /// The space a prune says it freed — <c>Total reclaimed space: 1.2GB</c> from <c>image prune</c> (and the legacy
    /// builder), <c>Total:\t12.3GB</c> from buildx — or <c>null</c> when it did not say.
    /// </summary>
    internal static string? Reclaimed(string output)
    {
        string[] prefixes = ["Total reclaimed space:", "Total:"];
        return output.Split('\n')
            .Select(line => line.Trim())
            .Select(line => prefixes.FirstOrDefault(p => line.StartsWith(p, StringComparison.Ordinal)) is { } prefix ? line[prefix.Length..].Trim() : null)
            .LastOrDefault(value => !string.IsNullOrEmpty(value));
    }

    /// <summary><c>docker system df</c>'s rows as (kind, reclaimable), skipping anything that isn't one.</summary>
    internal static IReadOnlyList<(string Kind, string Reclaimable)> ParseSystemDf(string output) =>
        [.. output.Split('\n')
            .Select(line => line.Trim('\r').Split('\t'))
            .Where(cells => cells.Length == 2 && cells[0].Length > 0 && cells[1].Length > 0)
            .Select(cells => (cells[0].Trim(), cells[1].Trim()))];
}
