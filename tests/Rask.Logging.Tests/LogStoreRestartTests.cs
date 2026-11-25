using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Rask.Logging.Tests;

/// <summary>
///     The one shape of the file store that needs a FILE: what a restarted process finds. Every other test runs
///     the store on a database in memory; this one is about the file outliving the process that wrote it.
/// </summary>
public sealed class LogStoreRestartTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-logs-restart-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_path}";

    /// <summary>
    /// The whole point of the package. A second store over the same file — the state a restarted process
    /// finds — still sees what the first one wrote, and creating its schema again is a no-op rather than an
    /// error.
    /// </summary>
    [Fact]
    public async Task Stored_entries_outlive_the_store_that_wrote_them()
    {
        var harness = new LoggingHarness(connectionString: ConnectionString);
        await using (harness)
        {
            harness.Logger("Shop").LogError("the thing that broke");
            await harness.RunUntilStoredAsync(1);
        }

        var reopened = new SqliteLogStore(ConnectionString, new LogsOptions(), harness.Clock);
        var page = await reopened.Search(new LogQuery(), TestContext.Current.CancellationToken);

        var entry = Assert.Single(page.Entries);
        Assert.Equal("the thing that broke", entry.Message);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Shop", entry.Category);
    }

    public void Dispose()
    {
        // The store's connections are pooled by connection string; the file stays locked until they are
        // released, and on Windows a delete would otherwise fail.
        using (var connection = new SqliteConnection(ConnectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

        foreach (var path in new[] { _path, $"{_path}-wal", $"{_path}-shm" })
        {
            File.Delete(path);
        }
    }
}
