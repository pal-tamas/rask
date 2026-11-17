using static Rask.Cli.Commands.PruneCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// Both prunes (<see cref="PruneCommands"/>) run on a box under its deploy lock — so never while another app's deploy is
/// switching the shared proxy — saying what each freed. What a deploy does once its new version serves, and what
/// <c>rask host clean</c> does on demand.
/// </summary>
internal sealed class HostCleanUp(IConsole console, IProcessRunner process, string workingDirectory)
{
    /// <summary>How a clean-up ended: done, without the lock (already said why), or failed (and why).</summary>
    internal enum Outcome
    {
        Cleaned,
        NoLock,
        Failed,
    }

    /// <summary>
    /// Take the box's lock with <paramref name="timing"/> — <paramref name="giveUp"/> styles what is said when it can't
    /// be had — prune down to <paramref name="keep"/> of build cache, and let the lock go, whatever happened.
    /// </summary>
    internal async Task<(Outcome Outcome, string? Failure)> RunAsync(string host, string owner, string keep, LockTiming timing, ConsoleStyle giveUp, CancellationToken cancellationToken)
    {
        string? failure;
        var boxLock = DeployLock.ForBox(console, process, workingDirectory, timing, giveUp);
        try
        {
            var held = await boxLock.AcquireAsync(host, owner, cancellationToken).ConfigureAwait(false);
            if (held is null)
            {
                return (Outcome.NoLock, null);
            }

            try
            {
                failure = await PruneAsync(host, keep, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await boxLock.ReleaseAsync(host, held).ConfigureAwait(false);
            }
        }
        catch (TimeoutException stalled)
        {
            failure = stalled.Message;
        }

        return (failure is null ? Outcome.Cleaned : Outcome.Failed, failure);
    }

    /// <summary>The two commands that free the space by hand — what is printed when a deploy can't, or didn't.</summary>
    internal async Task WriteFreeSpaceCommandsAsync(string host, string keep)
    {
        await console.Error.WriteLineAsync($"  rask host clean --host {host}     (both prunes, under the box's deploy lock; or by hand:)").ConfigureAwait(false);
        await console.Error.WriteLineAsync($"  docker -H ssh://{host} image prune -f").ConfigureAwait(false);
        await console.Error.WriteLineAsync($"  docker -H ssh://{host} builder prune -af --keep-storage {keep}").ConfigureAwait(false);
        await console.Error.WriteLineAsync($"  (`docker -H ssh://{host} system df` shows what takes the space. -a is safe on `builder prune`, which removes no image; never `image prune -a`, which deletes the :{DeployCommand.PreviousTag} images rollback needs)").ConfigureAwait(false);
    }

    /// <summary>
    /// Both prunes, in turn, saying what each freed — and what the disk gained, which is the number that matters: with
    /// the containerd image store <c>image prune</c> counts only the image records, not the layers it lets go. The
    /// first failure's reason, or <c>null</c>.
    /// </summary>
    private async Task<string?> PruneAsync(string host, string keep, CancellationToken cancellationToken)
    {
        var before = await HostDisk.ReadAsync(process, host, cancellationToken).ConfigureAwait(false);
        var images = await Capture(BuildImagePruneArguments(host), cancellationToken).ConfigureAwait(false);
        if (images.ExitCode != 0)
        {
            return $"docker image prune: {FirstLineOf(images.StandardError)}";
        }

        var cache = await Capture(BuildBuilderPruneArguments(host, keep), cancellationToken).ConfigureAwait(false);
        if (cache.ExitCode != 0 && IsUnknownFlag(cache.StandardError))
        {
            cache = await Capture(BuildBuilderPruneArguments(host, keep, LegacyBuildCacheFlag), cancellationToken).ConfigureAwait(false);
        }

        var after = await HostDisk.ReadAsync(process, host, cancellationToken).ConfigureAwait(false);
        var gained = before is { } b && after is { } a
            ? $" Now {a.Describe()} ({DiskSpace.Format(Math.Max(0, a.FreeBytes - b.FreeBytes))} freed)."
            : string.Empty;
        console.WriteLine(
            $"  Reclaimed {Reclaimed(images.StandardOutput) ?? "0B"} of old images"
            + (cache.ExitCode == 0 ? $" and {Reclaimed(cache.StandardOutput) ?? "0B"} of build cache." : ".")
            + gained,
            ConsoleStyle.Dim);
        return cache.ExitCode == 0 ? null : $"docker builder prune: {FirstLineOf(cache.StandardError)}";
    }

    private Task<ProcessResult> Capture(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        process.CaptureAsync("docker", args, workingDirectory, cancellationToken);

    private static string FirstLineOf(string text) => text.Trim().Split('\n')[0].Trim() is { Length: > 0 } line ? line : "no output";
}
