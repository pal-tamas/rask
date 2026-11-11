using static Rask.Cli.Commands.PruneCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// A deploy leaves the box no fuller than it found it: before it builds it checks there is room (<see cref="DiskSpace"/>),
/// and once the new version serves it removes what earlier deploys left behind (<see cref="PruneCommands"/>).
/// </summary>
internal sealed partial class DeployCommand
{
    /// <summary>How many attempts at the box's lock the clean-up makes before it leaves the work to the next deploy.</summary>
    internal const int CleanUpLockAttempts = 20;

    /// <summary>
    /// Whether the box has room to build on: <c>false</c> (said what to run) when it is nearly full, a warning when it
    /// is getting there, and nothing when it can't be read — a deploy is never stopped by a question it couldn't ask.
    /// </summary>
    private async Task<bool> HasRoomToBuildAsync(string host, CancellationToken cancellationToken)
    {
        if (await HostDisk.ReadAsync(_process, host, cancellationToken).ConfigureAwait(false) is not { } disk)
        {
            return true;
        }

        if (disk.IsTooLow)
        {
            Console.WriteErrorLine($"{HostName(host)} is nearly out of disk: {disk.Describe()}. Nothing was changed.", ConsoleStyle.Error);
            await Console.Error.WriteLineAsync($"A build needs room for its layers and the image, and a full disk stops every app on the box from writing its database. Free space, then deploy again:").ConfigureAwait(false);
            await WriteFreeSpaceCommandsAsync(host).ConfigureAwait(false);
            return false;
        }

        if (disk.IsLow)
        {
            Console.WriteErrorLine($"  ! {HostName(host)} is low on disk: {disk.Describe()}. This deploy cleans up old images and build cache once it succeeds.", ConsoleStyle.Warning);
        }

        return true;
    }

    private async Task WriteFreeSpaceCommandsAsync(string host)
    {
        await Console.Error.WriteLineAsync($"  docker -H ssh://{host} image prune -f").ConfigureAwait(false);
        await Console.Error.WriteLineAsync($"  docker -H ssh://{host} builder prune -f --keep-storage {BuildCacheKept}").ConfigureAwait(false);
        await Console.Error.WriteLineAsync($"  (`docker -H ssh://{host} system df` shows what takes the space; never `image prune -a`, which deletes the :{PreviousTag} images rollback needs)").ConfigureAwait(false);
    }

    /// <summary>
    /// Remove the images earlier deploys left untagged, and trim the build cache — after a deploy that succeeded, under
    /// the box's lock, so it never runs while another app's deploy is switching the shared proxy. Every failure here is
    /// a warning: the deploy already succeeded, and the next one tries again.
    /// </summary>
    private async Task CleanUpHostAsync(string host, string slug, CancellationToken cancellationToken)
    {
        await Console.Out.WriteLineAsync($"Cleaning up old images and build cache on {HostName(host)}…").ConfigureAwait(false);
        var boxLock = DeployLock.ForBox(Console, _process, _workingDirectory, new LockTiming(LockPollDelay, Math.Min(LockAttempts, CleanUpLockAttempts), TimeSpan.Zero), ConsoleStyle.Warning);
        string? failed;
        try
        {
            var held = await boxLock.AcquireAsync(host, slug, cancellationToken).ConfigureAwait(false);
            if (held is null)
            {
                Console.WriteErrorLine("  ! Skipped the clean-up — the next deploy does it.", ConsoleStyle.Warning);
                return;
            }

            try
            {
                failed = await PruneAsync(host, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await boxLock.ReleaseAsync(host, held).ConfigureAwait(false);
            }
        }
        catch (TimeoutException stalled)
        {
            failed = stalled.Message;
        }

        if (failed is not null)
        {
            Console.WriteErrorLine($"  ! Cleaning up failed ({failed}). The deploy itself succeeded; the next one tries again. To free space now:", ConsoleStyle.Warning);
            await WriteFreeSpaceCommandsAsync(host).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Both prunes, in turn, saying what each freed — and what the disk gained, which is the number that matters: with
    /// the containerd image store <c>image prune</c> counts only the image records, not the layers it lets go. The
    /// first failure's reason, or <c>null</c>.
    /// </summary>
    private async Task<string?> PruneAsync(string host, CancellationToken cancellationToken)
    {
        var before = await HostDisk.ReadAsync(_process, host, cancellationToken).ConfigureAwait(false);
        var images = await Capture(BuildImagePruneArguments(host), cancellationToken).ConfigureAwait(false);
        if (images.ExitCode != 0)
        {
            return $"docker image prune: {FirstLineOf(images.StandardError)}";
        }

        var cache = await Capture(BuildBuilderPruneArguments(host), cancellationToken).ConfigureAwait(false);
        if (cache.ExitCode != 0 && IsUnknownFlag(cache.StandardError))
        {
            cache = await Capture(BuildBuilderPruneArguments(host, LegacyBuildCacheFlag), cancellationToken).ConfigureAwait(false);
        }

        var after = await HostDisk.ReadAsync(_process, host, cancellationToken).ConfigureAwait(false);
        var gained = before is { } b && after is { } a
            ? $" Now {a.Describe()} ({DiskSpace.Format(Math.Max(0, a.FreeBytes - b.FreeBytes))} freed)."
            : string.Empty;
        Console.WriteLine(
            $"  Reclaimed {Reclaimed(images.StandardOutput) ?? "0B"} of old images"
            + (cache.ExitCode == 0 ? $" and {Reclaimed(cache.StandardOutput) ?? "0B"} of build cache." : ".")
            + gained,
            ConsoleStyle.Dim);
        return cache.ExitCode == 0 ? null : $"docker builder prune: {FirstLineOf(cache.StandardError)}";
    }

    /// <summary>The disk line of <c>rask deploy status</c>: free space, and what docker could give back.</summary>
    private async Task WriteDiskStatusAsync(string host, CancellationToken cancellationToken)
    {
        var disk = await HostDisk.ReadAsync(_process, host, cancellationToken).ConfigureAwait(false);
        var df = await Capture(BuildSystemDfArguments(host), cancellationToken).ConfigureAwait(false);
        var reclaimable = df.ExitCode == 0
            ? ParseSystemDf(df.StandardOutput).Where(row => row.Kind is "Images" or "Build Cache").ToList()
            : [];
        if (disk is null && reclaimable.Count == 0)
        {
            return;
        }

        var line = disk is { } d ? $"  Disk: {d.Describe()}." : "  Disk:";
        if (reclaimable.Count > 0)
        {
            line += $" Reclaimable: {string.Join(", ", reclaimable.Select(row => $"{row.Reclaimable} of {(string.Equals(row.Kind, "Images", StringComparison.Ordinal) ? "images" : "build cache")}"))} (every deploy prunes what is unused).";
        }

        Console.WriteLine(line, disk is { IsLow: true } ? ConsoleStyle.Warning : ConsoleStyle.Dim);
    }

    private static string FirstLineOf(string text) => text.Trim().Split('\n')[0].Trim() is { Length: > 0 } line ? line : "no output";
}
