using static Rask.Cli.Commands.PruneCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// A deploy leaves the box no fuller than it found it: before it builds it checks there is room (<see cref="DiskPolicy"/>),
/// and once the new version serves it removes what earlier deploys left behind (<see cref="HostCleanUp"/>).
/// </summary>
internal sealed partial class DeployCommand
{
    /// <summary>How many attempts at the box's lock the clean-up makes before it leaves the work to the next deploy.</summary>
    internal const int CleanUpLockAttempts = 20;

    /// <summary>The app's disk settings — <c>"buildCacheKeep"</c>, <c>"minFreeDisk"</c>, <c>"warnFreeDisk"</c> from <c>.rask/deploy.json</c>.</summary>
    internal DiskPolicy DiskPolicy { get; set; } = DiskPolicy.Default;

    private HostCleanUp CleanUp => new(Console, _process, _workingDirectory);

    /// <summary>
    /// Whether the box has room to build on: <c>false</c> (said what to run) when it is nearly full, a warning when it
    /// is getting there, and nothing when it can't be read — a deploy is never stopped by a question it couldn't ask.
    /// </summary>
    private async Task<bool> HasRoomToBuildAsync(string host, bool prunes, CancellationToken cancellationToken)
    {
        if (await HostDisk.ReadAsync(_process, host, cancellationToken).ConfigureAwait(false) is not { } disk)
        {
            return true;
        }

        if (DiskPolicy.IsTooLow(disk))
        {
            Console.WriteErrorLine($"{HostName(host)} is nearly out of disk: {disk.Describe()}. Nothing was changed.", ConsoleStyle.Error);
            await Console.Error.WriteLineAsync("A build needs room for its layers and the image, and a full disk stops every app on the box from writing its database. Free space, then deploy again:", cancellationToken).ConfigureAwait(false);
            await CleanUp.WriteFreeSpaceCommandsAsync(host, DiskPolicy.BuildCacheKeepArgument).ConfigureAwait(false);
            return false;
        }

        if (DiskPolicy.IsLow(disk))
        {
            Console.WriteErrorLine(
                $"  ! {HostName(host)} is low on disk: {disk.Describe()}. "
                + (prunes
                    ? "This deploy cleans up old images and build cache once it succeeds."
                    : $"This deploy skips its clean-up (--no-prune); `rask host clean --host {host}` frees space when you choose."),
                ConsoleStyle.Warning);
        }

        return true;
    }

    /// <summary>
    /// Remove the images earlier deploys left untagged, and trim the build cache — after a deploy that succeeded, under
    /// the box's lock, so it never runs while another app's deploy is switching the shared proxy. Every failure here is
    /// a warning: the deploy already succeeded, and the next one tries again.
    /// </summary>
    private async Task CleanUpHostAsync(string host, string slug, CancellationToken cancellationToken)
    {
        await Console.Out.WriteLineAsync($"Cleaning up old images and build cache on {HostName(host)}…", cancellationToken).ConfigureAwait(false);
        var cleanUp = CleanUp;
        var timing = new LockTiming(LockPollDelay, Math.Min(LockAttempts, CleanUpLockAttempts), TimeSpan.Zero);
        var (outcome, failure) = await cleanUp.RunAsync(host, slug, DiskPolicy.BuildCacheKeepArgument, timing, ConsoleStyle.Warning, cancellationToken).ConfigureAwait(false);
        if (outcome == HostCleanUp.Outcome.NoLock)
        {
            Console.WriteErrorLine("  ! Skipped the clean-up — the next deploy does it.", ConsoleStyle.Warning);
        }
        else if (outcome == HostCleanUp.Outcome.Failed)
        {
            Console.WriteErrorLine($"  ! Cleaning up failed ({failure}). The deploy itself succeeded; the next one tries again. To free space now:", ConsoleStyle.Warning);
            await cleanUp.WriteFreeSpaceCommandsAsync(host, DiskPolicy.BuildCacheKeepArgument).ConfigureAwait(false);
        }
    }

    /// <summary>The box's disk for <c>rask deploy status</c>: free space and what docker could give back, or <c>null</c> when neither could be read.</summary>
    private async Task<DeployDiskStatus?> ReadDiskStatusAsync(string host, CancellationToken cancellationToken)
    {
        var disk = await HostDisk.ReadAsync(_process, host, cancellationToken).ConfigureAwait(false);
        var df = await Capture(BuildSystemDfArguments(host), cancellationToken).ConfigureAwait(false);
        var rows = df.ExitCode == 0 ? ParseSystemDf(df.StandardOutput) : [];
        var images = Reclaimable(rows, "Images");
        var cache = Reclaimable(rows, "Build Cache");
        if (disk is null && images is null && cache is null)
        {
            return null;
        }

        return new DeployDiskStatus(
            disk?.Mount,
            disk?.FreeBytes,
            disk?.SizeBytes,
            disk is { } low && DiskPolicy.IsLow(low),
            disk is { } full && DiskPolicy.IsTooLow(full),
            images,
            cache);
    }

    private static string? Reclaimable(IReadOnlyList<(string Kind, string Reclaimable)> rows, string kind) =>
        rows.Where(row => string.Equals(row.Kind, kind, StringComparison.Ordinal)).Select(row => row.Reclaimable).FirstOrDefault();

    /// <summary>The disk line of <c>rask deploy status</c>: free space, and what docker could give back.</summary>
    private void WriteDiskStatus(string host, DeployDiskStatus? status)
    {
        if (status is null)
        {
            return;
        }

        var line = status is { FreeBytes: { } free, SizeBytes: { } size, Mount: { } mount }
            ? $"  Disk: {new DiskSpace(free, size, mount).Describe()}."
            : "  Disk:";
        var reclaimable = new[] { (status.ReclaimableImages, "images"), (status.ReclaimableBuildCache, "build cache") }
            .Where(kind => kind.Item1 is not null)
            .Select(kind => $"{kind.Item1} of {kind.Item2}")
            .ToList();
        if (reclaimable.Count > 0)
        {
            line += $" Reclaimable: {string.Join(", ", reclaimable)} (every deploy prunes what is unused; `rask host clean --host {host}` does it now).";
        }

        Console.WriteLine(line, status.IsLow ? ConsoleStyle.Warning : ConsoleStyle.Dim);
    }
}
