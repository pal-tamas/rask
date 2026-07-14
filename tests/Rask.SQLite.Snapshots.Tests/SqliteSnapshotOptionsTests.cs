using System.Reflection;

namespace Rask.SQLite.Snapshots.Tests;

public sealed class SqliteSnapshotOptionsTests
{
    [Fact]
    public void Defaults_are_sensible()
    {
        var options = new SqliteSnapshotOptions();

        Assert.Equal(TimeSpan.FromHours(6), options.Interval);
        Assert.Equal(7, options.Retain);
        Assert.False(options.SnapshotOnStartup);
        Assert.Equal(TimeSpan.FromSeconds(30), options.BusyTimeout);
    }

    [Fact]
    public void Validate_accepts_a_complete_configuration()
    {
        var options = new SqliteSnapshotOptions { DatabasePath = "/data/app.db", DestinationDirectory = "/backups" };
        Validate(options, requireDestinationDirectory: true); // does not throw
    }

    [Fact]
    public void Validate_requires_database_path()
    {
        var options = new SqliteSnapshotOptions { DestinationDirectory = "/backups" };
        Assert.Throws<InvalidOperationException>(() => Validate(options, requireDestinationDirectory: true));
    }

    [Fact]
    public void Validate_requires_destination_directory_for_the_default_store()
    {
        var options = new SqliteSnapshotOptions { DatabasePath = "/data/app.db" };
        Assert.Throws<InvalidOperationException>(() => Validate(options, requireDestinationDirectory: true));
    }

    [Fact]
    public void Validate_allows_missing_destination_with_a_custom_store()
    {
        var options = new SqliteSnapshotOptions { DatabasePath = "/data/app.db" };
        Validate(options, requireDestinationDirectory: false); // does not throw
    }

    [Fact]
    public void Validate_rejects_non_positive_interval()
    {
        var options = new SqliteSnapshotOptions
        {
            DatabasePath = "/data/app.db",
            DestinationDirectory = "/backups",
            Interval = TimeSpan.Zero,
        };
        Assert.Throws<InvalidOperationException>(() => Validate(options, requireDestinationDirectory: true));
    }

    [Fact]
    public void Validate_rejects_retain_below_one()
    {
        var options = new SqliteSnapshotOptions
        {
            DatabasePath = "/data/app.db",
            DestinationDirectory = "/backups",
            Retain = 0,
        };
        Assert.Throws<InvalidOperationException>(() => Validate(options, requireDestinationDirectory: true));
    }

    private static void Validate(SqliteSnapshotOptions options, bool requireDestinationDirectory)
    {
        var method = typeof(SqliteSnapshotOptions).GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            method.Invoke(options, [requireDestinationDirectory]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
