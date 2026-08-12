using Microsoft.Data.Sqlite;

namespace Rask.SQLite.Browser.Tests;

public class BrowserSqliteTests
{
    [Fact]
    public void DatabasePath_LivesUnderTheBrowserDirectory()
    {
        Assert.Equal("/rask/app.db", BrowserSqlite.DatabasePath("app"));
    }

    // Pooling is what returns a connection through sqlite3_close_v2's deactivation path, which
    // un-registers EF Core's user functions and yields SQLITE_BUSY on close.
    [Fact]
    public void ConnectionString_DisablesPooling()
    {
        var builder = new SqliteConnectionStringBuilder(BrowserSqlite.ConnectionString("app"));

        Assert.False(builder.Pooling);
        Assert.Equal("/rask/app.db", builder.DataSource);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_MustNotBeBlank(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => BrowserSqlite.DatabasePath(name));
    }

    // The name becomes a file name, an IndexedDB database name and a lock name at once, so a separator
    // would fail somewhere far away from the call that introduced it.
    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("../escape")]
    public void Name_MustNotContainAPathSeparator(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => BrowserSqlite.DatabasePath(name));

        Assert.Contains("path separator", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotStoreAndLockNames_AreScopedPerDatabase()
    {
        Assert.NotEqual(BrowserSqlite.SnapshotStoreName("a"), BrowserSqlite.SnapshotStoreName("b"));
        Assert.NotEqual(BrowserSqlite.OwnerLockName("a"), BrowserSqlite.OwnerLockName("b"));
    }
}

public class BrowserSqliteOptionsTests
{
    [Fact]
    public void Validate_ResolvesTheDatabasePathFromTheName()
    {
        var options = new BrowserSqliteOptions { Name = "jobs" };

        options.Validate();

        Assert.Equal("/rask/jobs.db", options.DatabasePath);
    }

    [Fact]
    public void Validate_KeepsAnExplicitDatabasePath()
    {
        var options = new BrowserSqliteOptions { Name = "jobs", DatabasePath = "/tmp/other.db" };

        options.Validate();

        Assert.Equal("/tmp/other.db", options.DatabasePath);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsANonPositiveInterval(int seconds)
    {
        var options = new BrowserSqliteOptions { SnapshotInterval = TimeSpan.FromSeconds(seconds) };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_RejectsRetainBelowOne()
    {
        var options = new BrowserSqliteOptions { Retain = 0 };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
