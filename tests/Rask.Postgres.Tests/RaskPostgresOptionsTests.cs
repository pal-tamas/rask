using System.Globalization;

namespace Rask.Postgres.Tests;

public class RaskPostgresOptionsTests
{
    [Fact]
    public void Defaults_AreTheProductionSet()
    {
        var options = new RaskPostgresOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.StatementTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.LockTimeout);
        Assert.Equal(TimeSpan.FromMinutes(1), options.IdleInTransactionSessionTimeout);
        Assert.Equal(6, options.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxRetryDelay);
    }

    [Fact]
    public void Defaults_Validate()
    {
        var exception = Record.Exception(() => new RaskPostgresOptions().Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void ANegativeTimeout_IsRejected()
    {
        var options = new RaskPostgresOptions { StatementTimeout = TimeSpan.FromSeconds(-1) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(RaskPostgresOptions.StatementTimeout), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANegativeRetryCount_IsRejected()
    {
        var options = new RaskPostgresOptions { MaxRetryCount = -1 };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(RaskPostgresOptions.MaxRetryCount), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALockTimeoutAtOrAboveTheStatementTimeout_IsRejected()
    {
        // The statement timeout would always fire first, so lock contention would be reported as a slow
        // query — the exact misdiagnosis the lock timeout exists to prevent.
        var options = new RaskPostgresOptions
        {
            StatementTimeout = TimeSpan.FromSeconds(10),
            LockTimeout = TimeSpan.FromSeconds(10),
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(RaskPostgresOptions.LockTimeout), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALockTimeoutAboveADisabledStatementTimeout_IsAllowed()
    {
        // With the statement timeout off there is nothing to fire first, so any lock timeout is meaningful.
        var options = new RaskPostgresOptions
        {
            StatementTimeout = TimeSpan.Zero,
            LockTimeout = TimeSpan.FromMinutes(5),
        };

        var exception = Record.Exception(options.Validate);

        Assert.Null(exception);
    }

    [Fact]
    public void BuildScript_EmitsEveryTimeoutAsIntegerMilliseconds()
    {
        var options = new RaskPostgresOptions
        {
            StatementTimeout = TimeSpan.FromSeconds(30),
            LockTimeout = TimeSpan.FromSeconds(10),
            IdleInTransactionSessionTimeout = TimeSpan.FromMinutes(1),
        };

        var script = PostgresSessionSettings.BuildScript(options);

        Assert.Equal(
            "SET statement_timeout = 30000;SET lock_timeout = 10000;SET idle_in_transaction_session_timeout = 60000;",
            script);
    }

    [Fact]
    public void BuildScript_OmitsADisabledTimeout()
    {
        // Zero means "leave it alone", so a server- or role-level setting wins rather than being
        // overwritten with the value it already had.
        var options = new RaskPostgresOptions
        {
            StatementTimeout = TimeSpan.Zero,
            LockTimeout = TimeSpan.FromSeconds(5),
            IdleInTransactionSessionTimeout = TimeSpan.Zero,
        };

        var script = PostgresSessionSettings.BuildScript(options);

        Assert.Equal("SET lock_timeout = 5000;", script);
    }

    [Fact]
    public void BuildScript_WithEveryTimeoutDisabled_IsEmpty()
    {
        var options = new RaskPostgresOptions
        {
            StatementTimeout = TimeSpan.Zero,
            LockTimeout = TimeSpan.Zero,
            IdleInTransactionSessionTimeout = TimeSpan.Zero,
        };

        Assert.Empty(PostgresSessionSettings.BuildScript(options));
    }

    [Fact]
    public void BuildScript_IsCultureInvariant()
    {
        // A culture that uses ',' as the decimal separator must not leak into the SQL.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("hu-HU");
            var script = PostgresSessionSettings.BuildScript(new RaskPostgresOptions());

            Assert.DoesNotContain(",", script, StringComparison.Ordinal);
            Assert.Contains("SET statement_timeout = 30000;", script, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void BuildScript_RoundsSubMillisecondPrecisionAway()
    {
        var options = new RaskPostgresOptions
        {
            StatementTimeout = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * 3 / 2),
            LockTimeout = TimeSpan.Zero,
            IdleInTransactionSessionTimeout = TimeSpan.Zero,
        };

        Assert.Equal("SET statement_timeout = 2;", PostgresSessionSettings.BuildScript(options));
    }
}
