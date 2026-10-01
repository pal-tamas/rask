using System.ComponentModel;

namespace Rask.Outbox;

/// <summary>
/// Wakes the <see cref="OutboxProcessor{TContext}"/> the moment a save commits outbox rows, so a durable handler
/// runs right after the change rather than on the next poll. The poll stays as the safety net: for rows another
/// instance wrote, for a backlog, and for a wake that fired before its transaction committed.
/// </summary>
/// <remarks>Public only because the processor's constructor names it; nothing outside the outbox uses it.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class OutboxSignal : IDisposable
{
    // At most one pending wake: ten saves before the processor looks are one cycle, not ten.
    private readonly SemaphoreSlim _wake = new(0, 1);

    internal OutboxSignal()
    {
    }

    /// <inheritdoc/>
    public void Dispose() => _wake.Dispose();

    internal void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already awake — the next cycle takes this save's rows too.
        }
    }

    /// <summary>Waits for a wake or for <paramref name="poll"/> to pass, whichever comes first.</summary>
    internal Task Wait(TimeSpan poll, CancellationToken cancellationToken) => _wake.WaitAsync(poll, cancellationToken);
}
