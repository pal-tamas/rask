using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Rask.Logging.Tests;

/// <summary>
///     Scope capture: the request id, user id and correlation id an application opens a scope with, stored
///     alongside the entry so the log can answer "what else happened on that request?" without anyone
///     having to reconstruct it from message text.
/// </summary>
public sealed class LogScopeTests
{
    [Fact]
    public async Task A_scope_is_stored_with_the_entry_it_wrapped()
    {
        await using var harness = new LoggingHarness();
        var logger = harness.Logger();

        using (logger.BeginScope(new Dictionary<string, object?> { ["RequestId"] = "abc123" }))
        {
            logger.LogInformation("inside");
        }

        logger.LogInformation("outside");
        await harness.RunUntilStoredAsync(2);

        var page = await harness.Store.SearchAsync(new LogQuery());
        var inside = page.Entries.Single(e => e.Message == "inside");
        var outside = page.Entries.Single(e => e.Message == "outside");

        Assert.NotNull(inside.Scopes);
        Assert.Equal("abc123", inside.Scopes!.Single(s => s.Key == "RequestId").Value);
        // The scope closed before this one was written — capturing it here would be worse than capturing
        // nothing, because it would attribute the entry to a request it did not belong to.
        Assert.Null(outside.Scopes);
    }

    [Fact]
    public async Task Nested_scopes_are_flattened_outermost_first()
    {
        await using var harness = new LoggingHarness();
        var logger = harness.Logger();

        using (logger.BeginScope(new Dictionary<string, object?> { ["RequestId"] = "r1" }))
        using (logger.BeginScope(new Dictionary<string, object?> { ["UserId"] = "u9" }))
        {
            logger.LogWarning("nested");
        }

        await harness.RunUntilStoredAsync(1);

        var entry = Assert.Single((await harness.Store.SearchAsync(new LogQuery())).Entries);
        Assert.NotNull(entry.Scopes);
        Assert.Equal("r1", entry.Scopes!.Single(s => s.Key == "RequestId").Value);
        Assert.Equal("u9", entry.Scopes.Single(s => s.Key == "UserId").Value);
    }

    [Fact]
    public async Task A_message_template_scope_keeps_its_values_and_drops_the_template()
    {
        await using var harness = new LoggingHarness();
        var logger = harness.Logger();

        using (logger.BeginScope("request {RequestId} for {UserId}", "r2", "u7"))
        {
            logger.LogError("boom");
        }

        await harness.RunUntilStoredAsync(1);

        var entry = Assert.Single((await harness.Store.SearchAsync(new LogQuery())).Entries);
        Assert.NotNull(entry.Scopes);
        Assert.Equal("r2", entry.Scopes!.Single(s => s.Key == "RequestId").Value);
        Assert.Equal("u7", entry.Scopes.Single(s => s.Key == "UserId").Value);
        // "{OriginalFormat}" is the template, not data — storing it would repeat the format string on
        // every row that used it.
        Assert.DoesNotContain(entry.Scopes, s => s.Key == "{OriginalFormat}");
    }

    [Fact]
    public async Task Querying_by_scope_finds_one_request_out_of_many()
    {
        await using var harness = new LoggingHarness();
        var logger = harness.Logger();

        foreach (var id in new[] { "r1", "r2", "r3" })
        {
            using (logger.BeginScope(new Dictionary<string, object?> { ["RequestId"] = id }))
            {
                logger.LogInformation("work for {Id}", id);
            }
        }

        await harness.RunUntilStoredAsync(3);

        var mine = await harness.Store.SearchAsync(new LogQuery { ScopeKey = "RequestId", ScopeValue = "r2" });
        var entry = Assert.Single(mine.Entries);
        Assert.Equal("work for r2", entry.Message);

        // Key alone finds every entry that carried it, which is the "which entries are request-scoped at
        // all?" question.
        var anyRequest = await harness.Store.SearchAsync(new LogQuery { ScopeKey = "RequestId" });
        Assert.Equal(3, anyRequest.Entries.Count);

        // A value that belongs to a different key must not match. This is why the filter uses
        // json_extract rather than a LIKE over the raw column.
        var wrongKey = await harness.Store.SearchAsync(new LogQuery { ScopeKey = "UserId", ScopeValue = "r2" });
        Assert.Empty(wrongKey.Entries);
    }

    [Fact]
    public async Task Capture_can_be_turned_off()
    {
        await using var harness = new LoggingHarness(o => o.CaptureScopes = false);
        var logger = harness.Logger();

        using (logger.BeginScope(new Dictionary<string, object?> { ["Secret"] = "value" }))
        {
            logger.LogInformation("quiet");
        }

        await harness.RunUntilStoredAsync(1);

        var entry = Assert.Single((await harness.Store.SearchAsync(new LogQuery())).Entries);
        Assert.Null(entry.Scopes);
    }

    [Fact]
    public async Task Capture_is_bounded_in_count_and_length()
    {
        await using var harness = new LoggingHarness(o =>
        {
            o.MaxScopeValues = 2;
            o.MaxScopeValueLength = 4;
        });
        var logger = harness.Logger();

        using (logger.BeginScope(new Dictionary<string, object?> { ["A"] = "aaaaaaaaaa" }))
        using (logger.BeginScope(new Dictionary<string, object?> { ["B"] = "bbbbbbbbbb" }))
        using (logger.BeginScope(new Dictionary<string, object?> { ["C"] = "cccccccccc" }))
        {
            logger.LogInformation("bounded");
        }

        await harness.RunUntilStoredAsync(1);

        var entry = Assert.Single((await harness.Store.SearchAsync(new LogQuery())).Entries);
        Assert.NotNull(entry.Scopes);
        Assert.Equal(2, entry.Scopes!.Count);                 // the third scope is dropped
        Assert.All(entry.Scopes, s => Assert.Equal(4, s.Value.Length)); // each value truncated
    }

    /// <summary>
    ///     The store shipped before scopes existed, so a database created by that release has a RaskLog
    ///     table with no Scopes column — and <c>CREATE TABLE IF NOT EXISTS</c> does nothing about it. Without
    ///     the migration every insert fails with "no such column" and the whole log stops being written.
    /// </summary>
    [Fact]
    public async Task A_store_created_before_scopes_existed_is_migrated_rather_than_broken()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"rask-logs-legacy-{Guid.NewGuid():N}.db");
        try
        {
            // Exactly the pre-scopes schema.
            await using (var seed = new SqliteConnection($"Data Source={dbPath}"))
            {
                await seed.OpenAsync();
                var create = seed.CreateCommand();
                create.CommandText = """
                    CREATE TABLE RaskLog (
                        Id        INTEGER PRIMARY KEY,
                        Timestamp TEXT    NOT NULL,
                        Level     INTEGER NOT NULL,
                        Category  TEXT    NOT NULL,
                        EventId   INTEGER NOT NULL,
                        Message   TEXT    NOT NULL,
                        Exception TEXT
                    );
                    INSERT INTO RaskLog (Timestamp, Level, Category, EventId, Message, Exception)
                    VALUES ('2026-01-01T00:00:00.0000000Z', 2, 'Old.Category', 0, 'from the old schema', NULL);
                    """;
                await create.ExecuteNonQueryAsync();
            }

            var options = new RaskLoggingOptions();
            var store = new SqliteLogStore($"Data Source={dbPath}", options, TimeProvider.System);

            await store.AppendAsync(
                [new LogRecord(0, DateTimeOffset.UtcNow, LogLevel.Information, "New.Category", 0, "after upgrade", null,
                    [new LogScopeValue("RequestId", "r9")])]);

            var page = await store.SearchAsync(new LogQuery());
            Assert.Equal(2, page.Entries.Count);

            // The old row survives with no scopes, and the new one round-trips its own.
            Assert.Null(page.Entries.Single(e => e.Message == "from the old schema").Scopes);
            var upgraded = page.Entries.Single(e => e.Message == "after upgrade");
            Assert.Equal("r9", upgraded.Scopes!.Single().Value);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }
}
