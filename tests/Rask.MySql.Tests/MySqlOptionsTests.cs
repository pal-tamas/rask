using System.Globalization;

namespace Rask.MySql.Tests;

public sealed class MySqlOptionsTests
{
    [Fact]
    public void Defaults_are_the_production_set()
    {
        var options = new MySqlOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.StatementTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.LockTimeout);
        Assert.True(options.Retry.Enabled);
        Assert.Equal(6, options.Retry.MaxCount);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Retry.MaxDelay);
    }

    [Fact]
    public void Defaults_validate()
    {
        Assert.Null(Record.Exception(new MySqlOptions().Validate));
    }

    [Fact]
    public void A_non_positive_command_timeout_is_rejected()
    {
        var options = new MySqlOptions { CommandTimeout = TimeSpan.Zero };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("MySqlOptions.CommandTimeout must be positive", exception.Message);
    }

    [Fact]
    public void A_negative_statement_timeout_is_rejected()
    {
        var options = new MySqlOptions { StatementTimeout = TimeSpan.FromSeconds(-1) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("MySqlOptions.StatementTimeout must not be negative", exception.Message);
    }

    [Fact]
    public void A_negative_lock_timeout_is_rejected()
    {
        var options = new MySqlOptions { LockTimeout = TimeSpan.FromSeconds(-1) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("MySqlOptions.LockTimeout must not be negative", exception.Message);
    }

    [Fact]
    public void A_lock_timeout_at_or_above_the_command_timeout_is_rejected()
    {
        // The client would give up first, so lock contention would be reported as a slow query.
        var options = new MySqlOptions
        {
            CommandTimeout = TimeSpan.FromSeconds(10),
            LockTimeout = TimeSpan.FromSeconds(10),
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("MySqlOptions.LockTimeout (00:00:10) must be below CommandTimeout", exception.Message);
    }

    [Fact]
    public void A_statement_timeout_beyond_a_32_bit_millisecond_count_is_rejected()
    {
        var options = new MySqlOptions { CommandTimeout = TimeSpan.FromDays(20), StatementTimeout = TimeSpan.FromDays(30) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("MySqlOptions.StatementTimeout must be at most 24.20:31:23.6470000", exception.Message);
    }

    [Fact]
    public void A_command_timeout_beyond_what_the_driver_honours_is_rejected()
    {
        var options = new MySqlOptions { CommandTimeout = TimeSpan.FromSeconds((int.MaxValue / 1000) + 1) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("MySqlOptions.CommandTimeout must be at most 24.20:31:23 ", exception.Message);
    }

    [Fact]
    public void The_largest_command_timeout_the_driver_honours_is_accepted()
    {
        var options = new MySqlOptions { CommandTimeout = TimeSpan.FromSeconds(int.MaxValue / 1000) };

        Assert.Null(Record.Exception(options.Validate));
    }

    [Fact]
    public void A_lock_timeout_beyond_mysqls_maximum_is_rejected()
    {
        var options = new MySqlOptions { LockTimeout = TimeSpan.FromSeconds(1_073_741_825) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("MySqlOptions.LockTimeout must be at most", exception.Message);
    }

    [Fact]
    public void A_lock_timeout_that_rounds_up_to_the_command_timeout_is_rejected()
    {
        // Both are sent as 10 whole seconds, so the lock wait could never fire first.
        var options = new MySqlOptions
        {
            CommandTimeout = TimeSpan.FromSeconds(10),
            LockTimeout = TimeSpan.FromMilliseconds(9_500),
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("MySqlOptions.LockTimeout (00:00:09.5000000) must be below CommandTimeout", exception.Message);
    }

    [Fact]
    public void An_enabled_retry_needs_at_least_one_attempt()
    {
        var options = new MySqlOptions();
        options.Retry.MaxCount = 0;

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("Turn retrying off with o.Retry.Enabled = false", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_retry_is_not_validated()
    {
        var options = new MySqlOptions();
        options.Retry.Enabled = false;
        options.Retry.MaxCount = 0;

        Assert.Null(Record.Exception(options.Validate));
    }

    [Fact]
    public void The_script_sets_both_timeouts_in_one_statement()
    {
        Assert.Equal(
            "SET SESSION innodb_lock_wait_timeout = 10, SESSION max_execution_time = 30000;",
            MySqlSessionSettings.BuildScript(new MySqlOptions()));
    }

    [Fact]
    public void A_setting_left_to_the_server_is_omitted()
    {
        var options = new MySqlOptions { StatementTimeout = TimeSpan.Zero };

        Assert.Equal("SET SESSION innodb_lock_wait_timeout = 10;", MySqlSessionSettings.BuildScript(options));
    }

    [Fact]
    public void With_every_setting_left_to_the_server_the_script_is_empty()
    {
        var options = new MySqlOptions { StatementTimeout = TimeSpan.Zero, LockTimeout = TimeSpan.Zero };

        Assert.Empty(MySqlSessionSettings.BuildScript(options));
    }

    [Fact]
    public void A_sub_second_lock_timeout_rounds_up_to_a_whole_second()
    {
        // innodb_lock_wait_timeout takes whole seconds with a minimum of 1; rounding down would send 0, which MySQL
        // clamps with a warning rather than honouring the configured value.
        var options = new MySqlOptions { StatementTimeout = TimeSpan.Zero, LockTimeout = TimeSpan.FromMilliseconds(200) };

        Assert.Equal("SET SESSION innodb_lock_wait_timeout = 1;", MySqlSessionSettings.BuildScript(options));
    }

    [Fact]
    public void A_sub_millisecond_statement_timeout_rounds_up_rather_than_to_no_limit()
    {
        var options = new MySqlOptions
        {
            LockTimeout = TimeSpan.Zero,
            StatementTimeout = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond / 4),
        };

        Assert.Equal("SET SESSION max_execution_time = 1;", MySqlSessionSettings.BuildScript(options));
    }

    [Fact]
    public void The_script_is_culture_invariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("hu-HU");

            Assert.Equal(
                "SET SESSION innodb_lock_wait_timeout = 10, SESSION max_execution_time = 30000;",
                MySqlSessionSettings.BuildScript(new MySqlOptions()));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
