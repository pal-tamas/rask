namespace Rask.Data;

/// <summary>
///     A test's turn at an environment database: held from before its schema is brought up until after it is dropped,
///     so one test at a time points the process-wide <see cref="Db" /> at a database — and, shared, one test at a time
///     on the whole machine uses it.
/// </summary>
internal sealed class TestDatabaseTurn : IAsyncDisposable
{
    // Db and ReadDb are process-wide, so two databases at once would each point every read at whichever came last.
    private static readonly SemaphoreSlim InProcess = new(1, 1);

    private static readonly TimeSpan FirstRetry = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromMilliseconds(500);

    private readonly FileStream? _machineLock;
    private Func<Task>? _drop;

    private TestDatabaseTurn(FileStream? machineLock) => _machineLock = machineLock;

    /// <summary>Waits for this process's turn, and with <paramref name="lockFile" /> the machine's too.</summary>
    public static async Task<TestDatabaseTurn> Take(string? lockFile, CancellationToken cancellationToken)
    {
        await InProcess.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var machineLock = lockFile is null ? null : await LockMachineWide(lockFile, cancellationToken).ConfigureAwait(false);
            return new TestDatabaseTurn(machineLock);
        }
        catch
        {
            InProcess.Release();
            throw;
        }
    }

    /// <summary>Drops the test's own database when the turn ends — after the fixture let go of it, before the next turn.</summary>
    public void DropOnEnd(Func<Task> drop) => _drop = drop;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_drop is not null)
            {
                await _drop().ConfigureAwait(false);
            }
        }
        finally
        {
            if (_machineLock is not null)
            {
                await _machineLock.DisposeAsync().ConfigureAwait(false);
            }

            InProcess.Release();
        }
    }

    /// <summary>
    ///     Opens <paramref name="path" /> exclusively. On Unix .NET backs <see cref="FileShare.None" /> with
    ///     <c>flock(LOCK_EX)</c>, the lock <c>lockf</c>/<c>flock</c> scripts take; on Windows it is a share mode.
    /// </summary>
    private static async Task<FileStream> LockMachineWide(string path, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var wait = FirstRetry;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
            }
            catch (IOException)
            {
                // Held by another test run: wait, backing off, until it lets go or the test is cancelled.
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                wait = wait * 2 < LongestRetry ? wait * 2 : LongestRetry;
            }
        }
    }
}
