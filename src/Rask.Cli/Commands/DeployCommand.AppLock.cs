namespace Rask.Cli.Commands;

/// <summary>
/// Two deploys of one app take turns: each holds the app's own lock from before it moves the image tags and picks a
/// colour until the old colour is retired. Deploys of different apps never wait on it — only on the short box lock
/// around what they share.
/// </summary>
internal sealed partial class DeployCommand
{
    /// <summary>How many attempts at an app lock another deploy of the app holds — enough to wait out a long build (45 minutes).</summary>
    internal int AppLockAttempts { get; set; } = 900;

    /// <summary>How often a held app lock's lease is renewed. Zero turns the renewal off (tests).</summary>
    internal TimeSpan AppLockHeartbeat { get; set; } = TimeSpan.FromSeconds(ProxyCommands.AppLockHeartbeatSeconds);

    /// <summary>Run <paramref name="deploy"/> holding <paramref name="slug"/>'s app lock; 1 when the lock can't be had (said why).</summary>
    private async Task<int> WithAppLockAsync(string host, string slug, Func<Task<int>> deploy, CancellationToken cancellationToken)
    {
        var appLock = DeployLock.ForApp(Console, _process, _workingDirectory, slug, new LockTiming(LockPollDelay, AppLockAttempts, AppLockHeartbeat));
        var held = await appLock.AcquireAsync(host, slug, cancellationToken).ConfigureAwait(false);
        if (held is null)
        {
            return 1;
        }

        try
        {
            return await deploy().ConfigureAwait(false);
        }
        finally
        {
            await appLock.ReleaseAsync(host, held).ConfigureAwait(false);
        }
    }
}
