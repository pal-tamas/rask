using System.Reflection;

namespace Rask.SQLite.Tests;

public sealed class SqlitePragmaOptionsTests
{
    [Fact]
    public void Defaults_match_the_rails_production_pragma_set()
    {
        var options = new SqlitePragmaOptions();

        Assert.Equal(SqliteJournalMode.Wal, options.JournalMode);
        Assert.Equal(SqliteSynchronous.Normal, options.Synchronous);
        Assert.Equal(true, options.ForeignKeys);
        Assert.Equal(TimeSpan.FromSeconds(5), options.BusyTimeout);
        Assert.Equal(2000, options.CacheSize);
        Assert.Equal(134_217_728, options.MmapSize);
        Assert.Equal(67_108_864, options.JournalSizeLimit);
        Assert.Null(options.TempStore);
    }

    [Fact]
    public void Validate_rejects_negative_busy_timeout()
    {
        var options = new SqlitePragmaOptions { BusyTimeout = TimeSpan.FromSeconds(-1) };
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_rejects_busy_timeout_over_int_max_milliseconds()
    {
        var options = new SqlitePragmaOptions { BusyTimeout = TimeSpan.FromDays(30) };
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_rejects_negative_mmap_size()
    {
        var options = new SqlitePragmaOptions { MmapSize = -1 };
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_rejects_negative_journal_size_limit()
    {
        var options = new SqlitePragmaOptions { JournalSizeLimit = -1 };
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_accepts_all_pragmas_disabled()
    {
        var options = new SqlitePragmaOptions
        {
            JournalMode = null,
            Synchronous = null,
            ForeignKeys = null,
            BusyTimeout = null,
            CacheSize = null,
            MmapSize = null,
            JournalSizeLimit = null,
            TempStore = null,
        };

        Validate(options); // does not throw
    }

    // Validate is internal (it runs inside the DI/EF entry points); exercise it directly via reflection.
    private static void Validate(SqlitePragmaOptions options)
    {
        var method = typeof(SqlitePragmaOptions).GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            method.Invoke(options, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
