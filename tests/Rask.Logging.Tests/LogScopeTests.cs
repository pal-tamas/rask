using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Rask.Logging.Tests;

public sealed class DbContextStoreLogScopeTests() : LogScopeContract(LogStoreKind.DbContext);

public sealed class FileStoreLogScopeTests() : LogScopeContract(LogStoreKind.File)
{
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

/// <summary>
///     Scope capture: the request id, user id and correlation id an application opens a scope with, stored
///     alongside the entry so the log can answer "what else happened on that request?" without anyone
///     having to reconstruct it from message text. A contract, run against both stores.
/// </summary>
public abstract class LogScopeContract(LogStoreKind kind)
{
    [Fact]
    public async Task A_scope_is_stored_with_the_entry_it_wrapped()
    {
        await using var harness = Harness();
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
        await using var harness = Harness();
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
        await using var harness = Harness();
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
        await using var harness = Harness();
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

        // A value that belongs to a different key must not match.
        var wrongKey = await harness.Store.SearchAsync(new LogQuery { ScopeKey = "UserId", ScopeValue = "r2" });
        Assert.Empty(wrongKey.Entries);
    }

    /// <summary>
    ///     The scope filter is exact, not a text search: a key that only appears in a message, or inside another
    ///     scope's value spelled to look like a scope, is not a match.
    /// </summary>
    [Fact]
    public async Task A_scope_key_that_only_appears_in_text_does_not_match()
    {
        await using var harness = Harness();
        var logger = harness.Logger();

        logger.LogInformation("RequestId r2 is only mentioned here");
        using (logger.BeginScope(new Dictionary<string, object?> { ["Note"] = "\"RequestId\":\"r2\"" }))
        {
            logger.LogInformation("a value that spells a scope");
        }

        await harness.RunUntilStoredAsync(2);

        Assert.Empty((await harness.Store.SearchAsync(new LogQuery { ScopeKey = "RequestId" })).Entries);
        Assert.Empty((await harness.Store.SearchAsync(new LogQuery { ScopeKey = "RequestId", ScopeValue = "r2" })).Entries);
    }

    [Fact]
    public async Task A_scope_value_matches_whole_not_as_a_prefix()
    {
        await using var harness = Harness();
        var logger = harness.Logger();

        foreach (var id in new[] { "r2", "r22" })
        {
            using (logger.BeginScope(new Dictionary<string, object?> { ["RequestId"] = id }))
            {
                logger.LogInformation("work for {Id}", id);
            }
        }

        await harness.RunUntilStoredAsync(2);

        var entry = Assert.Single(
            (await harness.Store.SearchAsync(new LogQuery { ScopeKey = "RequestId", ScopeValue = "r2" })).Entries);
        Assert.Equal("work for r2", entry.Message);
    }

    /// <summary>
    ///     A key is data the application chose, not an identifier: a space, a quote or an accent in it must find the
    ///     entry rather than break the query.
    /// </summary>
    [Fact]
    public async Task Scope_keys_and_values_with_spaces_quotes_and_accents_are_found()
    {
        await using var harness = Harness();
        var logger = harness.Logger();
        const string key = "Vevő \"azonosító\"";
        const string value = "Árvíztűrő \"tükörfúrógép\"";

        using (logger.BeginScope(new Dictionary<string, object?> { [key] = value }))
        {
            logger.LogInformation("unusual scope");
        }

        await harness.RunUntilStoredAsync(1);

        var entry = Assert.Single(
            (await harness.Store.SearchAsync(new LogQuery { ScopeKey = key, ScopeValue = value })).Entries);
        Assert.Equal(value, entry.Scopes!.Single(s => s.Key == key).Value);
    }

    /// <summary>
    ///     A scope key is data, not a path. The file store once looked keys up with
    ///     <c>json_extract(Scopes, '$.' || key)</c>: a dotted or bracketed key was read as a nested path and matched
    ///     nothing, and a key starting with a double quote made the query throw.
    /// </summary>
    [Theory]
    [InlineData("user.id")]
    [InlineData("items[0]")]
    [InlineData("\"quoted")]
    public async Task A_scope_key_shaped_like_a_json_path_is_found(string key)
    {
        await using var harness = Harness();
        var logger = harness.Logger();

        using (logger.BeginScope(new Dictionary<string, object?> { [key] = "v1" }))
        {
            logger.LogInformation("keyed");
        }

        logger.LogInformation("unscoped");
        await harness.RunUntilStoredAsync(2);

        Assert.Equal(
            "keyed",
            Assert.Single((await harness.Store.SearchAsync(new LogQuery { ScopeKey = key })).Entries).Message);
        Assert.Equal(
            "keyed",
            Assert.Single((await harness.Store.SearchAsync(new LogQuery { ScopeKey = key, ScopeValue = "v1" })).Entries).Message);
    }

    [Fact]
    public async Task Capture_can_be_turned_off()
    {
        await using var harness = Harness(o => o.CaptureScopes = false);
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
        await using var harness = Harness(o =>
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

    private LoggingHarness Harness(Action<RaskLoggingOptions>? configure = null) => new(configure, kind: kind);
}
