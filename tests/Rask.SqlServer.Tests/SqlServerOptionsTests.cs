using System.Globalization;

namespace Rask.SqlServer.Tests;

public sealed class SqlServerOptionsTests
{
    [Fact]
    public void Defaults_are_the_production_set()
    {
        var options = new SqlServerOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.LockTimeout);
        Assert.True(options.AbortOnError);
        Assert.True(options.Retry.Enabled);
        Assert.Equal(6, options.Retry.MaxCount);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Retry.MaxDelay);
    }

    [Fact]
    public void Defaults_validate()
    {
        Assert.Null(Record.Exception(new SqlServerOptions().Validate));
    }

    [Fact]
    public void A_non_positive_command_timeout_is_rejected()
    {
        // It is the only ceiling there is — SQL Server has no server-side statement timeout — and SqlClient reads
        // 0 as "wait forever", so "no timeout" cannot be spelled as zero here.
        var options = new SqlServerOptions { CommandTimeout = TimeSpan.Zero };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("SqlServerOptions.CommandTimeout must be positive", exception.Message);
    }

    [Fact]
    public void A_negative_lock_timeout_is_rejected()
    {
        var options = new SqlServerOptions { LockTimeout = TimeSpan.FromSeconds(-1) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("SqlServerOptions.LockTimeout must not be negative", exception.Message);
    }

    [Fact]
    public void A_lock_timeout_beyond_a_32_bit_millisecond_count_is_rejected()
    {
        var options = new SqlServerOptions
        {
            CommandTimeout = TimeSpan.FromDays(60),
            LockTimeout = TimeSpan.FromDays(30),
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("SqlServerOptions.LockTimeout must be at most 24.20:31:23.6470000", exception.Message);
    }

    [Fact]
    public void A_lock_timeout_at_or_above_the_command_timeout_is_rejected()
    {
        // The client would give up first, so lock contention would be reported as a slow query — the exact
        // misdiagnosis the lock timeout exists to prevent.
        var options = new SqlServerOptions
        {
            CommandTimeout = TimeSpan.FromSeconds(10),
            LockTimeout = TimeSpan.FromSeconds(10),
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("SqlServerOptions.LockTimeout (00:00:10) must be below CommandTimeout", exception.Message);
    }

    [Fact]
    public void A_zero_lock_timeout_means_wait_indefinitely_and_sends_nothing()
    {
        var options = new SqlServerOptions { LockTimeout = TimeSpan.Zero };

        Assert.Null(Record.Exception(options.Validate));
        Assert.Equal("SET XACT_ABORT ON;", SqlServerSessionSettings.BuildScript(options));
    }

    [Fact]
    public void An_enabled_retry_needs_at_least_one_attempt()
    {
        var options = new SqlServerOptions();
        options.Retry.MaxCount = 0;

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("Turn retrying off with o.Retry.Enabled = false", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_retry_is_not_validated()
    {
        var options = new SqlServerOptions();
        options.Retry.Enabled = false;
        options.Retry.MaxCount = 0;

        Assert.Null(Record.Exception(options.Validate));
    }

    [Fact]
    public void The_script_sets_xact_abort_and_the_lock_timeout_in_milliseconds()
    {
        Assert.Equal("SET XACT_ABORT ON;SET LOCK_TIMEOUT 10000;", SqlServerSessionSettings.BuildScript(new SqlServerOptions()));
    }

    [Fact]
    public void The_script_omits_xact_abort_when_turned_off()
    {
        var options = new SqlServerOptions { AbortOnError = false };

        Assert.Equal("SET LOCK_TIMEOUT 10000;", SqlServerSessionSettings.BuildScript(options));
    }

    [Fact]
    public void With_nothing_to_set_the_script_is_empty()
    {
        var options = new SqlServerOptions { AbortOnError = false, LockTimeout = TimeSpan.Zero };

        Assert.Empty(SqlServerSessionSettings.BuildScript(options));
    }

    [Fact]
    public void A_sub_millisecond_lock_timeout_rounds_up_rather_than_to_no_wait()
    {
        // SET LOCK_TIMEOUT 0 means "fail the instant you meet a lock" — the opposite of a short wait.
        var options = new SqlServerOptions
        {
            AbortOnError = false,
            LockTimeout = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond / 4),
        };

        Assert.Equal("SET LOCK_TIMEOUT 1;", SqlServerSessionSettings.BuildScript(options));
    }

    [Fact]
    public void A_sub_second_command_timeout_rounds_up_rather_than_to_wait_forever()
    {
        Assert.Equal(1, SqlServerSessionSettings.Seconds(TimeSpan.FromMilliseconds(400)));
    }

    [Fact]
    public void The_script_is_culture_invariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("hu-HU");

            Assert.Equal("SET XACT_ABORT ON;SET LOCK_TIMEOUT 10000;", SqlServerSessionSettings.BuildScript(new SqlServerOptions()));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
