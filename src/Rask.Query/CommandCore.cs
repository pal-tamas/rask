using System.Diagnostics.CodeAnalysis;
using Rask.Core.Live;

namespace Rask.Querying;

/// <summary>
///     The shared state machine behind every command shape — a record command and a function alike:
///     pending/error/success, the components watching it, and the optimistic edits of a send to roll back.
/// </summary>
/// <remarks>
///     A command once sent runs to its end under its own token (<see cref="CommandToken" />): the caller's token only
///     says whether anyone is still waiting for the answer. One that is not gets none — no error, the status it had
///     before — as a browser drops the answer to a request its page has left.
/// </remarks>
internal sealed class CommandCore
{
    private readonly ComponentReaders _readers = new();

    public CommandStatus Status { get; private set; } = CommandStatus.Idle;

    public Exception? Error { get; private set; }

    public void Observe() => _readers.Observe();

    public void Reset()
    {
        Status = CommandStatus.Idle;
        Error = null;
        _readers.RenderAll();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Whatever the handler threw belongs on the command as Error, for the component "
                        + "to render. See the remarks on Command<TCommand>.Send for why it is not rethrown.")]
    public async Task<TResult?> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> dispatch,
        Action invalidate,
        OptimisticEdit[] optimistic,
        CancellationToken cancellationToken)
    {
        // A caller already gone has nothing to send for: never sent, and nothing on screen moves.
        if (cancellationToken.IsCancellationRequested)
        {
            return default;
        }

        var before = (Status, Error);
        Status = CommandStatus.Pending;
        Error = null;
        _readers.RenderAll();

        // Snapshots first, and all of them, before anything is dispatched: a rollback that only
        // covers the edits made before the failure leaves the rest applied.
        var snapshots = optimistic.Length == 0 ? [] : new IOptimisticSnapshot[optimistic.Length];
        for (var i = 0; i < optimistic.Length; i++)
        {
            snapshots[i] = optimistic[i].Apply();
        }

        // Sent under its own token, not the caller's: a page that leaves drops the answer, never the write half-way.
        using var own = CommandToken.Start();
        try
        {
            var sending = dispatch(own.Token);

            // A hook's services are its event's for a grace after its component has left; a command it sent keeps them
            // to the end.
            (Ambient.Services as EventServices)?.Keep(sending);
            var result = await sending.ConfigureAwait(false);
            (Status, Error) = Left(cancellationToken, own.Token) ? before : (CommandStatus.Success, null);

            // The invalidation replaces the optimistic guess with what the server actually holds, so
            // there is nothing to undo on success.
            invalidate();
            _readers.RenderAll();
            return result;
        }
        catch (OperationCanceledException) when (Left(cancellationToken, own.Token))
        {
            // Cancelled by nothing of the command's own after its caller left, so whether the write landed is not
            // known: nothing is undone, and the queries it makes out of date ask the server.
            (Status, Error) = before;
            invalidate();
            _readers.RenderAll();
            return default;
        }
        catch (Exception ex)
        {
            // Undone in reverse, so overlapping edits to one entry unwind in the order they were made.
            for (var i = snapshots.Length - 1; i >= 0; i--)
            {
                snapshots[i].Restore();
            }

            (Status, Error) = Left(cancellationToken, own.Token) ? before : (CommandStatus.Error, ex);
            _readers.RenderAll();
            return default;
        }
    }

    // The caller went away — the page left, the component unmounted — and the command's own limit (its handler
    // timeout, the host stopping) did not end it: the answer is dropped silently, the status is what it was.
    private static bool Left(CancellationToken caller, CancellationToken own) =>
        caller.IsCancellationRequested && !own.IsCancellationRequested;
}
