using Microsoft.Data.Sqlite;

namespace Rask.SQLite.Browser.Tests;

public class BrowserSqliteTests
{
    [Fact]
    public void The_database_path_lives_under_the_browser_directory()
    {
        Assert.Equal("/rask/app.db", BrowserSqlite.DatabasePath("app"));
    }

    // Pooling is what returns a connection through sqlite3_close_v2's deactivation path, which
    // un-registers EF Core's user functions and yields SQLITE_BUSY on close.
    [Fact]
    public void The_connection_string_disables_pooling()
    {
        var builder = new SqliteConnectionStringBuilder(BrowserSqlite.ConnectionString("app"));

        Assert.False(builder.Pooling);
        Assert.Equal("/rask/app.db", builder.DataSource);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_rejected(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => BrowserSqlite.DatabasePath(name));
    }

    // The name becomes a file name, an IndexedDB database name and a lock name at once, so a separator
    // would fail somewhere far away from the call that introduced it.
    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("../escape")]
    public void A_name_containing_a_path_separator_is_rejected(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => BrowserSqlite.DatabasePath(name));

        Assert.Contains("path separator", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_store_and_lock_names_are_scoped_per_database()
    {
        Assert.NotEqual(BrowserSqlite.SnapshotStoreName("a"), BrowserSqlite.SnapshotStoreName("b"));
        Assert.NotEqual(BrowserSqlite.OwnerLockName("a"), BrowserSqlite.OwnerLockName("b"));
    }
}

public class BrowserSqliteOptionsTests
{
    [Fact]
    public void Validating_resolves_the_database_path_from_the_name()
    {
        var options = new BrowserSqliteOptions { Name = "jobs" };

        options.Validate();

        Assert.Equal("/rask/jobs.db", options.DatabasePath);
    }

    [Fact]
    public void Validating_keeps_an_explicit_database_path()
    {
        var options = new BrowserSqliteOptions { Name = "jobs", DatabasePath = "/tmp/other.db" };

        options.Validate();

        Assert.Equal("/tmp/other.db", options.DatabasePath);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validating_rejects_a_non_positive_snapshot_interval(int seconds)
    {
        var options = new BrowserSqliteOptions { SnapshotInterval = TimeSpan.FromSeconds(seconds) };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validating_rejects_a_retain_count_below_one()
    {
        var options = new BrowserSqliteOptions { Retain = 0 };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
