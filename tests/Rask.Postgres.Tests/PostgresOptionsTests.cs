using System.Globalization;
using Npgsql;

namespace Rask.Postgres.Tests;

public sealed class PostgresOptionsTests
{
    private const string AllDefaults =
        "-c statement_timeout=30000 -c lock_timeout=10000 -c idle_in_transaction_session_timeout=60000";

    [Fact]
    public void Defaults_are_the_production_set()
    {
        var options = new PostgresOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.StatementTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.LockTimeout);
        Assert.Equal(TimeSpan.FromMinutes(1), options.IdleInTransactionSessionTimeout);
        Assert.True(options.Retry.Enabled);
        Assert.Equal(6, options.Retry.MaxCount);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Retry.MaxDelay);
    }

    [Fact]
    public void Defaults_validate()
    {
        Assert.Null(Record.Exception(new PostgresOptions().Validate));
    }

    [Fact]
    public void A_negative_timeout_is_rejected()
    {
        var options = new PostgresOptions { StatementTimeout = TimeSpan.FromSeconds(-1) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("PostgresOptions.StatementTimeout must not be negative", exception.Message);
    }

    [Fact]
    public void A_timeout_beyond_a_32_bit_millisecond_count_is_rejected_at_startup()
    {
        // PostgreSQL would refuse it on every connection open, in production, rather than here.
        var options = new PostgresOptions { IdleInTransactionSessionTimeout = TimeSpan.FromDays(30) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("PostgresOptions.IdleInTransactionSessionTimeout must be at most 24.20:31:23.6470000", exception.Message);
    }

    [Fact]
    public void The_largest_32_bit_millisecond_count_is_allowed()
    {
        var options = new PostgresOptions
        {
            StatementTimeout = TimeSpan.Zero,
            IdleInTransactionSessionTimeout = TimeSpan.FromMilliseconds(int.MaxValue),
        };

        Assert.Null(Record.Exception(options.Validate));
    }

    [Fact]
    public void A_lock_timeout_at_or_above_the_statement_timeout_is_rejected()
    {
        // The statement timeout would always fire first, so lock contention would be reported as a slow
        // query — the exact misdiagnosis the lock timeout exists to prevent.
        var options = new PostgresOptions
        {
            StatementTimeout = TimeSpan.FromSeconds(10),
            LockTimeout = TimeSpan.FromSeconds(10),
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("PostgresOptions.LockTimeout (00:00:10) must be below StatementTimeout", exception.Message);
    }

    [Fact]
    public void A_lock_timeout_above_a_server_owned_statement_timeout_is_allowed()
    {
        var options = new PostgresOptions { StatementTimeout = TimeSpan.Zero, LockTimeout = TimeSpan.FromMinutes(5) };

        Assert.Null(Record.Exception(options.Validate));
    }

    [Fact]
    public void An_enabled_retry_needs_at_least_one_attempt()
    {
        var options = new PostgresOptions();
        options.Retry.MaxCount = 0;

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("Turn retrying off with o.Retry.Enabled = false", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_retry_is_not_validated()
    {
        var options = new PostgresOptions();
        options.Retry.Enabled = false;
        options.Retry.MaxCount = 0;

        Assert.Null(Record.Exception(options.Validate));
    }

    [Fact]
    public void An_enabled_retry_needs_a_positive_delay()
    {
        var options = new PostgresOptions();
        options.Retry.MaxDelay = TimeSpan.Zero;

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.StartsWith("PostgresOptions.Retry.MaxDelay must be positive", exception.Message);
    }

    [Fact]
    public void The_startup_options_carry_every_timeout_as_integer_milliseconds()
    {
        Assert.Equal(AllDefaults, PostgresSessionSettings.BuildStartupOptions(new PostgresOptions()));
    }

    [Fact]
    public void A_timeout_left_to_the_server_is_omitted()
    {
        var options = new PostgresOptions
        {
            StatementTimeout = TimeSpan.Zero,
            LockTimeout = TimeSpan.FromSeconds(5),
            IdleInTransactionSessionTimeout = TimeSpan.Zero,
        };

        Assert.Equal("-c lock_timeout=5000", PostgresSessionSettings.BuildStartupOptions(options));
    }

    [Fact]
    public void A_sub_millisecond_timeout_rounds_up_rather_than_to_no_limit()
    {
        // PostgreSQL reads 0 as "no limit": rounding to nearest would turn the shortest timeout into none at all.
        var options = new PostgresOptions
        {
            StatementTimeout = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond / 4),
            LockTimeout = TimeSpan.Zero,
            IdleInTransactionSessionTimeout = TimeSpan.Zero,
        };

        Assert.Equal("-c statement_timeout=1", PostgresSessionSettings.BuildStartupOptions(options));
    }

    [Fact]
    public void The_startup_options_are_culture_invariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("hu-HU");

            Assert.Equal(AllDefaults, PostgresSessionSettings.BuildStartupOptions(new PostgresOptions()));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Applying_adds_the_startup_options_to_the_connection_string()
    {
        var applied = PostgresSessionSettings.Apply("Host=db;Database=app;Username=app", new PostgresOptions());

        var builder = new NpgsqlConnectionStringBuilder(applied);
        Assert.Equal(AllDefaults, builder.Options);
        Assert.Equal("db", builder.Host);
        Assert.Equal("app", builder.Database);
    }

    [Fact]
    public void A_connection_string_that_already_sets_options_keeps_them_and_they_win()
    {
        // PostgreSQL applies a repeated -c in order, so the string's own values go last.
        var options = new PostgresOptions
        {
            StatementTimeout = TimeSpan.FromSeconds(5),
            LockTimeout = TimeSpan.Zero,
            IdleInTransactionSessionTimeout = TimeSpan.Zero,
        };

        var applied = PostgresSessionSettings.Apply("Host=db;Options=-c search_path=app -c statement_timeout=900", options);

        Assert.Equal(
            "-c statement_timeout=5000 -c search_path=app -c statement_timeout=900",
            new NpgsqlConnectionStringBuilder(applied).Options);
    }

    [Fact]
    public void With_every_timeout_left_to_the_server_the_connection_string_is_untouched()
    {
        var options = new PostgresOptions
        {
            StatementTimeout = TimeSpan.Zero,
            LockTimeout = TimeSpan.Zero,
            IdleInTransactionSessionTimeout = TimeSpan.Zero,
        };

        const string connectionString = "Host=db;Database=app";
        Assert.Same(connectionString, PostgresSessionSettings.Apply(connectionString, options));
    }
}
