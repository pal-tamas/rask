namespace Rask.SqlServer.Tests;

/// <summary>A server that answers with <paramref name="read"/>, counting how often it is asked.</summary>
internal sealed class FakeLevelReader(Func<string, int?> read) : ISqlServerLevelReader
{
    private int _reads;

    /// <summary>How many times the server was asked.</summary>
    public int Reads => Volatile.Read(ref _reads);

    public int? Read(string connectionString)
    {
        Interlocked.Increment(ref _reads);
        return read(connectionString);
    }
}
