using System.Globalization;
using static Rask.Cli.Commands.ProxyCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// The box-wide lock around everything deploys share: creating the network and the proxy, switching the proxy, and
/// retiring the colour it switched from. It is a container named <c>rask-deploy-lock</c>: docker refuses a second
/// container of one name, so taking it is atomic, and it removes itself when its lease ends, so a deploy that died
/// holding it cannot wedge the box. Never held across a build, a start or a health gate.
/// </summary>
internal sealed class DeployLock(IConsole console, IProcessRunner process, string workingDirectory)
{
    /// <summary>How long to wait between attempts at a lock another deploy holds — zero in tests.</summary>
    internal TimeSpan PollDelay { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How many attempts before giving up: past a full lease, so a holder that died is outlived.</summary>
    internal int Attempts { get; init; } = (LockLeaseSeconds / 3) + 20;

    /// <summary>Take the lock for <paramref name="slug"/>'s deploy, waiting while another deploy holds it; <c>null</c> (said why) when it can't be had.</summary>
    internal async Task<string?> AcquireAsync(string host, string slug, CancellationToken cancellationToken)
    {
        var owner = $"{slug} from {Environment.MachineName} at {DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)}";
        var holder = string.Empty;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (attempt > 0 && PollDelay > TimeSpan.Zero)
            {
                await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
            }

            var taken = await Capture(BuildLockArguments(host, owner), cancellationToken).ConfigureAwait(false);
            if (taken.ExitCode == 0)
            {
                // The id, not the name, is what releases it: see BuildUnlockArguments.
                var id = taken.StandardOutput.Trim();
                return id.Length > 0 ? id : LockContainer;
            }

            if (!IsHeld(taken.StandardError))
            {
                console.WriteErrorLine($"Could not take the deploy lock on {host}: {FirstLine(taken.StandardError)}", ConsoleStyle.Error);
                return null;
            }

            if (holder.Length == 0)
            {
                holder = (await Capture(BuildLockOwnerArguments(host), cancellationToken).ConfigureAwait(false)).StandardOutput.Trim();
                console.WriteLine($"  Another deploy is switching the proxy on this box ({Describe(holder)}) — waiting for it…", ConsoleStyle.Dim);
            }
        }

        console.WriteErrorLine($"Gave up waiting for the deploy lock on {host}, held by {Describe(holder)}.", ConsoleStyle.Error);
        console.WriteErrorLine($"If no deploy is running, free it: docker -H ssh://{host} rm -f {LockContainer}", ConsoleStyle.Error);
        return null;
    }

    /// <summary>Release a lock this deploy took. Not cancellable: a cancelled deploy must still let go of the box.</summary>
    internal async Task ReleaseAsync(string host, string lockId)
    {
        var released = await Capture(BuildUnlockArguments(host, lockId), CancellationToken.None).ConfigureAwait(false);
        if (released.ExitCode != 0)
        {
            console.WriteErrorLine($"Releasing the deploy lock failed ({FirstLine(released.StandardError)}) — it frees itself within {LockLeaseSeconds / 60} minutes.", ConsoleStyle.Warning);
        }
    }

    // docker run with a name in use: "Conflict. The container name "/rask-deploy-lock" is already in use by container …".
    private static bool IsHeld(string error) =>
        error.Contains("is already in use", StringComparison.Ordinal) || error.Contains("Conflict", StringComparison.Ordinal);

    private static string Describe(string holder) => holder.Length > 0 ? holder : "a deploy that didn't say who it is";

    private static string FirstLine(string text) => text.Trim().Split('\n')[0].Trim();

    private Task<ProcessResult> Capture(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        process.CaptureAsync("docker", args, workingDirectory, cancellationToken);
}
