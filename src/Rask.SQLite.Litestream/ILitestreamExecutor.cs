namespace Rask.SQLite.Litestream;

/// <summary>
/// Runs the <c>litestream</c> executable with a set of arguments. Abstracted so the restore and
/// replication logic can be unit-tested with a fake, without the real binary. The default
/// implementation shells out to a process; cancelling the token terminates it (used both to time-box a
/// restore and to stop the long-running <c>replicate</c> on shutdown).
/// </summary>
public interface ILitestreamExecutor
{
    /// <summary>Runs <c>litestream</c> with <paramref name="arguments"/> and returns its exit code.</summary>
    Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
