using System.Diagnostics.CodeAnalysis;

namespace Rask.Querying;

/// <summary>
///     The shared state machine behind every command shape — a record command and a function alike:
///     pending/error/success, the components watching it, and the optimistic edits of a send to roll back.
/// </summary>
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

        try
        {
            var result = await dispatch(cancellationToken).ConfigureAwait(false);
            Status = CommandStatus.Success;

            // The invalidation replaces the optimistic guess with what the server actually holds, so
            // there is nothing to undo on success.
            invalidate();
            _readers.RenderAll();
            return result;
        }
        catch (Exception ex)
        {
            // Undone in reverse, so overlapping edits to one entry unwind in the order they were made.
            for (var i = snapshots.Length - 1; i >= 0; i--)
            {
                snapshots[i].Restore();
            }

            Error = ex;
            Status = CommandStatus.Error;
            _readers.RenderAll();
            return default;
        }
    }
}
