using System.Globalization;

namespace Rask.Cli.Commands;

/// <summary>
/// How much room the box has where docker keeps its images and build cache, in one read-only ssh round trip. Docker's
/// own root (<c>docker info</c>'s <c>DockerRootDir</c>) and containerd's (<c>/var/lib/containerd</c>, where the
/// containerd image store keeps image layers) are both asked, and the tighter filesystem is the one reported — on a
/// default install they are one disk, the one the apps' database volumes live on too.
/// </summary>
internal static class HostDisk
{
    /// <summary>
    /// <c>df -Pk</c> over both data roots: POSIX output, 1024-byte blocks, one line per filesystem. A root that does
    /// not exist is an error line on stderr and no row, so a box without containerd's reports docker's alone.
    /// </summary>
    internal const string Script = """
        d="$(docker info --format '{{.DockerRootDir}}' 2>/dev/null)"
        df -Pk ${d:+"$d"} /var/lib/containerd 2>/dev/null | awk 'NR > 1 && (m == "" || $4 + 0 < m + 0) { m = $4; s = $2; p = $6 } END { if (m != "") printf "freekb=%s\nsizekb=%s\nmount=%s\n", m, s, p }'
        """;

    internal static IReadOnlyList<string> BuildArguments(SshTarget target) =>
        [.. target.ConnectionArguments(), Script];

    /// <summary>The box's free space, or <c>null</c> when it could not be read — the caller goes on without it.</summary>
    internal static async Task<DiskSpace?> ReadAsync(IProcessRunner process, string host, CancellationToken cancellationToken)
    {
        if (!SshTarget.TryParse(host, out var target, out _))
        {
            return null;
        }

        var result = await process.CaptureAsync("ssh", BuildArguments(target), null, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? Parse(result.StandardOutput) : null;
    }

    /// <summary>The script's <c>key=value</c> lines; <c>null</c> unless both sizes are there and make sense.</summary>
    internal static DiskSpace? Parse(string output)
    {
        var values = output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains('=', StringComparison.Ordinal))
            .Select(line => line.Split('=', 2))
            .GroupBy(pair => pair[0], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last()[1], StringComparer.Ordinal);

        return Kilobytes(values, "freekb") is { } free && Kilobytes(values, "sizekb") is { } size && size > 0 && free <= size
            ? new DiskSpace(free * 1024, size * 1024, values.GetValueOrDefault("mount", "/"))
            : null;
    }

    private static long? Kilobytes(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var text) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var kb) ? kb : null;
}
