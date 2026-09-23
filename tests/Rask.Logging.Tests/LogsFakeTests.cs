using Microsoft.Extensions.Logging;
using Rask.Batteries;
using Rask.Testing;

namespace Rask.Logging.Tests;

/// <summary>The fake stands in for the whole store, so there is no database and no SQLite file.</summary>
public sealed class LogsFakeTests
{
    private static LogRecord Entry(string message, LogLevel level = LogLevel.Information, string category = "Shop.Orders") =>
        new(0, Clock.Now, level, category, EventId: 0, message, Exception: null);

    [Fact]
    public async Task A_fake_takes_every_write_instead_of_the_real_store()
    {
        using var logs = Logs.Fake();

        await Logs.Search(new LogQuery());
        await ((ILogs)logs).Append([Entry("saved")]);

        logs.Stored().Saying("saved").Once();
    }

    [Fact]
    public async Task The_steps_narrow_together()
    {
        using var logs = Logs.Fake();

        await ((ILogs)logs).Append([
            Entry("routine"),
            Entry("refused", LogLevel.Error),
            Entry("refused elsewhere", LogLevel.Error, "Shop.Billing"),
        ]);

        logs.Stored().Exactly(3);
        logs.Stored().AtLeast(LogLevel.Error).Twice();
        logs.Stored().AtLeast(LogLevel.Error).From("Orders").Once();
        logs.Stored().Saying("refused").From("Billing").Once();
        logs.Stored().AtLeast(LogLevel.Critical).None();
    }

    [Fact]
    public async Task What_was_written_is_searched_back_newest_first()
    {
        using var logs = Logs.Fake();
        await ((ILogs)logs).Append([Entry("first"), Entry("second")]);

        var page = await Logs.Search(new LogQuery());

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("second", page.Entries[0].Message);
    }

    [Fact]
    public async Task A_search_filters_by_level_and_text()
    {
        using var logs = Logs.Fake();
        await ((ILogs)logs).Append([Entry("routine"), Entry("refused", LogLevel.Error)]);

        var errors = await Logs.Search(new LogQuery { MinimumLevel = LogLevel.Error });
        var refused = await Logs.Search(new LogQuery { Search = "REFUS" });

        Assert.Equal("refused", Assert.Single(errors.Entries).Message);
        Assert.Equal("refused", Assert.Single(refused.Entries).Message);
    }

    [Fact]
    public async Task Categories_and_Count_read_what_is_there()
    {
        using var logs = Logs.Fake();
        await ((ILogs)logs).Append([Entry("a"), Entry("b", category: "Shop.Billing")]);

        Assert.Equal(["Shop.Billing", "Shop.Orders"], await Logs.Categories());
        Assert.Equal(2, await Logs.Count());
    }

    [Fact]
    public async Task Trim_drops_what_is_older_than_the_age_given()
    {
        using var clock = Clock.Fake(at: new DateTimeOffset(2026, 6, 10, 9, 00, 0, TimeSpan.Zero));
        using var logs = Logs.Fake();
        await ((ILogs)logs).Append([Entry("old")]);
        clock.Advance(40.Days);
        await ((ILogs)logs).Append([Entry("recent")]);

        var removed = await Logs.Trim().OlderThan(30.Days);

        Assert.Equal(1, removed);
        logs.Stored().Saying("recent").Once();
        logs.Stored().Saying("old").None();
    }

    [Fact]
    public async Task Trim_keeps_only_the_newest_rows_asked_for()
    {
        using var logs = Logs.Fake();
        await ((ILogs)logs).Append([Entry("1"), Entry("2"), Entry("3")]);

        var removed = await Logs.Trim().KeepingNewest(1);

        Assert.Equal(2, removed);
        Assert.Equal(1, await Logs.Count());
    }

    [Fact]
    public async Task Trim_with_no_step_removes_nothing()
    {
        using var logs = Logs.Fake();
        await ((ILogs)logs).Append([Entry("kept")]);

        Assert.Equal(0, await Logs.Trim());
        Assert.Equal(1, await Logs.Count());
    }

    [Fact]
    public void Trim_refuses_a_step_that_would_remove_everything()
    {
        using var logs = Logs.Fake();

        // Clear() says "remove everything" out loud; a zero here would say it by accident.
        Assert.Throws<ArgumentOutOfRangeException>(() => Logs.Trim().OlderThan(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Logs.Trim().KeepingNewest(0));
    }

    [Fact]
    public async Task A_failure_names_what_was_stored_instead()
    {
        using var logs = Logs.Fake();
        await ((ILogs)logs).Append([Entry("routine")]);

        var error = Assert.Throws<CountingException>(() => logs.Stored().AtLeast(LogLevel.Error).Once());

        Assert.Contains("routine", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsOn_is_true_while_a_fake_stands_in_and_false_with_no_app()
    {
        using (var logs = Logs.Fake())
        {
            Assert.True(Logs.IsOn);
        }

        // Outside any work in progress there is no app to ask, which is what the dashboard renders "off" for.
        Assert.False(Logs.IsOn);
    }

    [Fact]
    public async Task Disposing_the_fake_puts_the_real_store_back()
    {
        using (var logs = Logs.Fake())
        {
            await Logs.Count();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Logs.Count());
        Assert.Contains("Inject ILogs", error.Message, StringComparison.Ordinal);
    }
}
