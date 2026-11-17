using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     The board of a client of a server — the browser half of a <c>wasm-hosted</c> app (#1348): it asks the server for the
///     signed-in user's rows (<see cref="ReadOwnWork" />), which reads them from its own board under its own rules, and
///     learns from the answer how often they change.
/// </summary>
/// <remarks>
///     A caller the server does not know, or a server with no jobs to read, is an empty board rather than a failure: the
///     bell stands quiet, as it does on a server page with nobody signed in.
/// </remarks>
internal sealed class RemoteWorkBoard(IRemoteDispatch remote) : IWorkBoard
{
    /// <summary>How often the rows are read before the server has said: the job processor's own default poll.</summary>
    internal static TimeSpan Unread => TimeSpan.FromSeconds(5);

    private long _every = Unread.Ticks;

    /// <inheritdoc />
    public TimeSpan Every => TimeSpan.FromTicks(Interlocked.Read(ref _every));

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkReported>> Mine(string? lane, CancellationToken cancellationToken)
    {
        OwnWorkRead read;
        try
        {
            read = await remote.Send<OwnWorkRead>(OwnWorkWire.Rows, new ReadOwnWork(lane), cancellationToken).ConfigureAwait(false);
        }
        catch (RemoteDispatchException ex) when (ex.StatusCode is 401 or 403 or 404)
        {
            return [];
        }

        // A server with no board says zero: there is nothing to read again soon.
        if (read.Every > TimeSpan.Zero)
        {
            Interlocked.Exchange(ref _every, read.Every.Ticks);
        }

        return read.Work;
    }
}
