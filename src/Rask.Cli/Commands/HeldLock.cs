namespace Rask.Cli.Commands;

/// <summary>A deploy lock this deploy holds: the lock container's id, and the heartbeat renewing its lease, if it has one.</summary>
internal sealed class HeldLock(string id, CancellationTokenSource? heartbeat, Task beating)
{
    public string Id { get; } = id;

    /// <summary>Stop renewing the lease, and wait for the last renewal to finish.</summary>
    public async Task StopHeartbeatAsync()
    {
        if (heartbeat is null)
        {
            return;
        }

        await heartbeat.CancelAsync().ConfigureAwait(false);
        await beating.ConfigureAwait(false);
        heartbeat.Dispose();
    }
}
