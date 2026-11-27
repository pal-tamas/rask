using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Rask.Batteries;
using Rask.Tests.Shared;

namespace Rask.Logging.Tests;

/// <summary>
///     The file store's key: a version-7 <see cref="Guid" /> Rask makes before the insert, stored as the table's
///     primary key — no number the database hands out — and the full-text index bound to the rows through a number of
///     its own that VACUUM never moves. And a <c>logs.db</c> from the releases that keyed an entry by its rowid,
///     re-keyed on open under the ids it showed.
/// </summary>
public sealed class SqliteLogKeyTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_entry_is_keyed_by_a_version_7_guid_made_before_the_insert()
    {
        await using var database = new InMemorySqlite();
        var store = Store(database);

        await store.Append([Record("first"), Record("second")], TestContext.Current.CancellationToken);

        var entries = (await store.Search(new LogQuery(), TestContext.Current.CancellationToken)).Entries;
        Assert.All(entries, e => Assert.Equal(7, e.Id.Version));
        Assert.Equal(2, entries.Select(e => e.Id).Distinct().Count());
        Assert.Equal(
            [("ID", "TEXT", 1)],
            await Rows(database, "SELECT upper(name), type, pk FROM pragma_table_info('RaskLog') WHERE pk > 0;",
                r => (r.GetString(0), r.GetString(1), r.GetInt32(2))));
        Assert.Equal(
            entries.Select(e => e.Id).Order(),
            await Rows(database, "SELECT Id FROM RaskLog ORDER BY Id;", r => Guid.Parse(r.GetString(0))));
    }

    [Fact]
    public async Task Entries_come_newest_first_in_the_order_they_were_made()
    {
        await using var database = new InMemorySqlite();
        var store = Store(database);

        // Two batches, and inside each several entries in the same millisecond: the key alone orders them.
        await store.Append([Record("a"), Record("b"), Record("c")], TestContext.Current.CancellationToken);
        await store.Append([Record("d"), Record("e")], TestContext.Current.CancellationToken);

        var page = await store.Search(new LogQuery { PageSize = 2, Page = 2 }, TestContext.Current.CancellationToken);
        var all = await store.Search(new LogQuery(), TestContext.Current.CancellationToken);
        Assert.Equal(["e", "d", "c", "b", "a"], all.Entries.Select(e => e.Message));
        Assert.Equal(["c", "b"], page.Entries.Select(e => e.Message));
    }

    [Fact]
    public async Task Search_finds_entries_after_insert_purge_and_vacuum()
    {
        await using var database = new InMemorySqlite();
        var clock = new FakeTimeProvider(Start);
        var store = new SqliteLogStore(database.ConnectionString, new LogsOptions(), clock);
        await store.Append(
            [.. Enumerable.Range(0, 30).Select(i => Record($"old needle {i}", at: Start))],
            TestContext.Current.CancellationToken);
        await store.Append(
            [.. Enumerable.Range(0, 30).Select(i => Record($"new needle {i}", at: Start.AddDays(10)))],
            TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromDays(10));

        // The age purge takes the old half, the cap a third of the rest; VACUUM then rewrites the whole file.
        await store.Trim(TimeSpan.FromDays(5), 20, TestContext.Current.CancellationToken);
        await Execute(database, "VACUUM;");
        await store.Append([Record("newest needle", at: Start.AddDays(10))], TestContext.Current.CancellationToken);

        var found = await store.Search(new LogQuery { Search = "needle", PageSize = 100 }, TestContext.Current.CancellationToken);
        Assert.Equal(21, found.TotalCount);
        Assert.Equal("newest needle", found.Entries[0].Message);
        Assert.DoesNotContain(found.Entries, e => e.Message.StartsWith("old", StringComparison.Ordinal));
        Assert.Equal(
            "new needle 29",
            Assert.Single((await store.Search(new LogQuery { Search = "needle 29" }, TestContext.Current.CancellationToken)).Entries).Message);
        await Execute(database, "INSERT INTO RaskLogSearch (RaskLogSearch, rank) VALUES ('integrity-check', 1);");
    }

    [Fact]
    public async Task An_integer_keyed_file_is_rekeyed_on_open_keeping_the_ids_it_showed()
    {
        await using var database = new InMemorySqlite();
        // Exactly what the previous release wrote: the rowid key, and the full-text index bound to it.
        await Execute(database, """
            CREATE TABLE RaskLog (
                Id INTEGER PRIMARY KEY, Timestamp TEXT NOT NULL, Level INTEGER NOT NULL, Category TEXT NOT NULL,
                EventId INTEGER NOT NULL, Message TEXT NOT NULL, Exception TEXT, Scopes TEXT);
            CREATE INDEX IX_RaskLog_Timestamp ON RaskLog (Timestamp);
            CREATE INDEX IX_RaskLog_Level_Id ON RaskLog (Level, Id DESC);
            CREATE INDEX IX_RaskLog_Category_Id ON RaskLog (Category, Id DESC);
            CREATE VIRTUAL TABLE RaskLogSearch USING fts5(
                Message, Exception, content='RaskLog', content_rowid='Id', tokenize='trigram');
            CREATE TRIGGER TR_RaskLog_Search_Insert AFTER INSERT ON RaskLog BEGIN
                INSERT INTO RaskLogSearch (rowid, Message, Exception) VALUES (new.Id, new.Message, new.Exception);
            END;
            CREATE TRIGGER TR_RaskLog_Search_Delete AFTER DELETE ON RaskLog BEGIN
                INSERT INTO RaskLogSearch (RaskLogSearch, rowid, Message, Exception)
                VALUES ('delete', old.Id, old.Message, old.Exception);
            END;
            INSERT INTO RaskLog (Id, Timestamp, Level, Category, EventId, Message, Exception, Scopes) VALUES
                (7, '2026-01-01T00:00:00.0010000Z', 2, 'Shop', 0, 'old order shipped', NULL, '{"RequestId":"r1"}'),
                (8, '2026-01-01T00:00:00.0010000Z', 4, 'Shop', 3, 'old order failed', 'System.Exception: boom', NULL);
            """);
        var store = Store(database);

        await store.Append([Record("new order shipped", at: Start.AddDays(1))], TestContext.Current.CancellationToken);

        var entries = (await store.Search(new LogQuery(), TestContext.Current.CancellationToken)).Entries;
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, 1, TimeSpan.Zero);
        Assert.Equal(["new order shipped", "old order failed", "old order shipped"], entries.Select(e => e.Message));
        Assert.Equal([OrderedGuid.Of(at, 8), OrderedGuid.Of(at, 7)], entries.Skip(1).Select(e => e.Id));
        Assert.Equal("r1", entries[2].Scopes!.Single().Value);
        Assert.Equal(
            ["new order shipped", "old order shipped"],
            (await store.Search(new LogQuery { Search = "shipped" }, TestContext.Current.CancellationToken)).Entries.Select(e => e.Message));
        Assert.Equal(
            "old order failed",
            Assert.Single((await store.Search(new LogQuery { Search = "boom" }, TestContext.Current.CancellationToken)).Entries).Message);
        await Execute(database, "INSERT INTO RaskLogSearch (RaskLogSearch, rank) VALUES ('integrity-check', 1);");
    }

    private static SqliteLogStore Store(InMemorySqlite database) =>
        new(database.ConnectionString, new LogsOptions(), new FakeTimeProvider(Start));

    private static LogRecord Record(string message, DateTimeOffset? at = null) =>
        new(Guid.Empty, at ?? Start, LogLevel.Information, "Test", 0, message, null);

    private static async Task Execute(InMemorySqlite database, string sql)
    {
        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<T>> Rows<T>(InMemorySqlite database, string sql, Func<SqliteDataReader, T> read)
    {
        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(read(reader));
        }

        return rows;
    }
}
