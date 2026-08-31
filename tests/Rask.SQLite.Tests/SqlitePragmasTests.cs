namespace Rask.SQLite.Tests;

public sealed class SqlitePragmasTests
{
    [Fact]
    public void BuildScript_emits_the_production_defaults()
    {
        var script = SqlitePragmas.BuildScript(new SqliteOptions());

        Assert.Contains("PRAGMA foreign_keys=ON;", script);
        Assert.Contains("PRAGMA journal_mode=WAL;", script);
        Assert.Contains("PRAGMA synchronous=NORMAL;", script);
        Assert.Contains("PRAGMA busy_timeout=5000;", script);
        Assert.Contains("PRAGMA cache_size=2000;", script);
        Assert.Contains("PRAGMA mmap_size=134217728;", script);
        Assert.Contains("PRAGMA journal_size_limit=67108864;", script);

        // Hardening defaults: a schema that cannot invoke arbitrary functions, corruption caught at the
        // page that carries it, and a bounded PRAGMA optimize.
        Assert.Contains("PRAGMA trusted_schema=OFF;", script);
        Assert.Contains("PRAGMA cell_size_check=ON;", script);
        Assert.Contains("PRAGMA analysis_limit=400;", script);
    }

    [Fact]
    public void BuildScript_honors_hardening_overrides()
    {
        var options = new SqliteOptions
        {
            TrustedSchema = true,
            CellSizeCheck = false,
            AnalysisLimit = 0,
        };

        var script = SqlitePragmas.BuildScript(options);

        Assert.Contains("PRAGMA trusted_schema=ON;", script);
        Assert.Contains("PRAGMA cell_size_check=OFF;", script);
        Assert.Contains("PRAGMA analysis_limit=0;", script);
    }

    [Fact]
    public void A_negative_analysis_limit_is_rejected()
    {
        var options = new SqliteOptions { AnalysisLimit = -1 };
        var ex = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(SqliteOptions.AnalysisLimit), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_omits_temp_store_by_default()
    {
        var script = SqlitePragmas.BuildScript(new SqliteOptions());
        Assert.DoesNotContain("temp_store", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_skips_null_pragmas()
    {
        var options = new SqliteOptions { JournalMode = null, BusyTimeout = null };
        var script = SqlitePragmas.BuildScript(options);

        Assert.DoesNotContain("journal_mode", script, StringComparison.Ordinal);
        Assert.DoesNotContain("busy_timeout", script, StringComparison.Ordinal);
        Assert.Contains("foreign_keys", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_returns_empty_when_everything_disabled()
    {
        var options = new SqliteOptions
        {
            JournalMode = null,
            Synchronous = null,
            ForeignKeys = null,
            BusyTimeout = null,
            CacheSize = null,
            MmapSize = null,
            JournalSizeLimit = null,
            TempStore = null,
            TrustedSchema = null,
            CellSizeCheck = null,
            AnalysisLimit = null,
        };

        Assert.Empty(SqlitePragmas.BuildScript(options));
    }

    [Fact]
    public void BuildScript_honors_overrides()
    {
        var options = new SqliteOptions
        {
            ForeignKeys = false,
            JournalMode = SqliteJournalMode.Delete,
            CacheSize = -20_000,
            TempStore = SqliteTempStore.Memory,
        };

        var script = SqlitePragmas.BuildScript(options);

        Assert.Contains("PRAGMA foreign_keys=OFF;", script);
        Assert.Contains("PRAGMA journal_mode=DELETE;", script);
        Assert.Contains("PRAGMA cache_size=-20000;", script);
        Assert.Contains("PRAGMA temp_store=MEMORY;", script);
    }

    [Fact]
    public void BuildScript_rounds_busy_timeout_to_milliseconds()
    {
        var options = new SqliteOptions { BusyTimeout = TimeSpan.FromMilliseconds(2500) };
        Assert.Contains("PRAGMA busy_timeout=2500;", SqlitePragmas.BuildScript(options));
    }

    [Fact]
    public void BuildScript_emits_busy_timeout_before_journal_mode()
    {
        // busy_timeout must be set before the lock-taking journal_mode=WAL switch, or a concurrent
        // WAL init on a fresh database can hit SQLITE_BUSY with no wait.
        var script = SqlitePragmas.BuildScript(new SqliteOptions());
        Assert.True(
            script.IndexOf("busy_timeout", StringComparison.Ordinal) < script.IndexOf("journal_mode", StringComparison.Ordinal));
    }
}
