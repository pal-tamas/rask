using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Rask.Logging.Tests;

/// <summary>
///     The file store's search index (#1111): a trigram FTS5 table that serves substring search instead of a
///     <c>LIKE '%…%'</c> scan over every retained row. What a search MEANS is pinned by the store contract in
///     <see cref="LogQueryContract" />; these pin that the index is used, kept current and built for old stores.
/// </summary>
public sealed class SqliteLogSearchIndexTests
{
    [Fact]
    public async Task A_search_of_three_or_more_characters_is_served_by_the_index()
    {
        await using var harness = new LoggingHarness();
        harness.Logger().LogInformation("order 42 shipped");
        await harness.RunUntilStoredAsync(1);

        var plan = await PlanAsync(harness.DbPath, "\"shipped\"");

        Assert.Contains("RaskLogSearch VIRTUAL TABLE INDEX", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_shorter_search_still_finds_its_rows()
    {
        // A trigram index has nothing to look up for two characters, so that search falls back to the scan.
        await using var harness = new LoggingHarness();
        harness.Logger().LogInformation("id 7x");
        harness.Logger().LogInformation("nothing");
        await harness.RunUntilStoredAsync(2);

        Assert.Single((await harness.Store.SearchAsync(new LogQuery { Search = "7x" })).Entries);
    }

    [Fact]
    public async Task Two_characters_are_two_characters_however_they_are_encoded()
    {
        // "😀a" is three UTF-16 units but two characters — too few for a trigram, so it is the scan, and it matches.
        await using var harness = new LoggingHarness();
        harness.Logger().LogInformation("mood 😀a today");
        await harness.RunUntilStoredAsync(1);

        Assert.Single((await harness.Store.SearchAsync(new LogQuery { Search = "😀a" })).Entries);
    }

    [Fact]
    public async Task A_quote_in_the_search_is_literal_text()
    {
        await using var harness = new LoggingHarness();
        harness.Logger().LogInformation("user said \"hi there\"");
        harness.Logger().LogInformation("hi there");
        await harness.RunUntilStoredAsync(2);

        Assert.Single((await harness.Store.SearchAsync(new LogQuery { Search = "\"hi there\"" })).Entries);
    }

    [Fact]
    public async Task Retention_keeps_the_index_in_step_with_the_rows()
    {
        await using var harness = new LoggingHarness();
        harness.Logger().LogInformation("old needle");
        harness.Logger().LogInformation("new needle");
        await harness.RunUntilStoredAsync(2);

        // The row cap, not the age: it keeps the newest row and deletes the other through the same paged DELETE.
        await harness.Store.PurgeAsync(TimeSpan.FromDays(365), maxRows: 1);

        var hit = Assert.Single((await harness.Store.SearchAsync(new LogQuery { Search = "needle" })).Entries);
        Assert.Equal("new needle", hit.Message);
        await AssertIntegrityAsync(harness.DbPath);

        await harness.Store.ClearAsync();
        Assert.Empty((await harness.Store.SearchAsync(new LogQuery { Search = "needle" })).Entries);
        await AssertIntegrityAsync(harness.DbPath);
    }

    [Fact]
    public async Task A_store_written_before_the_index_is_backfilled_when_it_is_opened()
    {
        await using var harness = new LoggingHarness();
        harness.Logger().LogError(new InvalidOperationException("the needle"), "broke");
        await harness.RunUntilStoredAsync(1);

        // What a logs.db from the previous release looks like: the table, and no index beside it.
        await using (var raw = new SqliteConnection(harness.ConnectionString))
        {
            await raw.OpenAsync();
            await using var drop = raw.CreateCommand();
            drop.CommandText = """
                DROP TRIGGER TR_RaskLog_Search_Insert;
                DROP TRIGGER TR_RaskLog_Search_Delete;
                DROP TRIGGER TR_RaskLog_Search_Update;
                DROP TABLE RaskLogSearch;
                """;
            await drop.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        var reopened = new SqliteLogStore(harness.ConnectionString, new RaskLoggingOptions(), harness.Clock);

        Assert.Single((await reopened.SearchAsync(new LogQuery { Search = "needle" })).Entries);
        await AssertIntegrityAsync(harness.DbPath);
    }

    private static async Task<string> PlanAsync(string dbPath, string match)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "EXPLAIN QUERY PLAN SELECT Id FROM RaskLog WHERE Id IN "
            + "(SELECT rowid FROM RaskLogSearch WHERE RaskLogSearch MATCH $m) ORDER BY Id DESC";
        command.Parameters.AddWithValue("$m", match);

        var plan = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.Add(reader.GetString(3));
        }

        return string.Join('\n', plan);
    }

    // FTS5's own consistency check against the external content: fails if the index disagrees with the rows.
    private static async Task AssertIntegrityAsync(string dbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO RaskLogSearch (RaskLogSearch, rank) VALUES ('integrity-check', 1);";
        await command.ExecuteNonQueryAsync();
    }
}
