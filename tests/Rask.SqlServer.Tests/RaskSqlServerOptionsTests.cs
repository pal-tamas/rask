using System.Globalization;

namespace Rask.SqlServer.Tests;

public class RaskSqlServerOptionsTests
{
    [Fact]
    public void Defaults_AreTheProductionSet()
    {
        var options = new RaskSqlServerOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.LockTimeout);
        Assert.True(options.AbortOnError);
        Assert.Equal(6, options.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxRetryDelay);
    }

    [Fact]
    public void Defaults_Validate()
    {
        Assert.Null(Record.Exception(() => new RaskSqlServerOptions().Validate()));
    }

    [Fact]
    public void ANonPositiveCommandTimeout_IsRejected()
    {
        // It is the only ceiling there is — SQL Server has no server-side statement timeout — so "no
        // timeout" cannot be expressed by setting it to zero the way a PostgreSQL GUC would be.
        var options = new RaskSqlServerOptions { CommandTimeout = TimeSpan.Zero };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(RaskSqlServerOptions.CommandTimeout), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANegativeLockTimeout_IsRejected()
    {
        var options = new RaskSqlServerOptions { LockTimeout = TimeSpan.FromSeconds(-1) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(RaskSqlServerOptions.LockTimeout), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALockTimeoutAtOrAboveTheCommandTimeout_IsRejected()
    {
        // The client would give up first, so lock contention would be reported as a slow query — the exact
        // misdiagnosis the lock timeout exists to prevent.
        var options = new RaskSqlServerOptions
        {
            CommandTimeout = TimeSpan.FromSeconds(10),
            LockTimeout = TimeSpan.FromSeconds(10),
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(RaskSqlServerOptions.LockTimeout), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroLockTimeout_MeansWaitForever_AndIsAllowed()
    {
        var options = new RaskSqlServerOptions { LockTimeout = TimeSpan.Zero };

        Assert.Null(Record.Exception(options.Validate));
        Assert.DoesNotContain("LOCK_TIMEOUT", SqlServerSessionSettings.BuildScript(options), StringComparison.Ordinal);
    }

    [Fact]
    public void ANegativeRetryCount_IsRejected()
    {
        var options = new RaskSqlServerOptions { MaxRetryCount = -1 };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(RaskSqlServerOptions.MaxRetryCount), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_EmitsXactAbortAndTheLockTimeoutInMilliseconds()
    {
        var script = SqlServerSessionSettings.BuildScript(new RaskSqlServerOptions());

        Assert.Equal("SET XACT_ABORT ON;SET LOCK_TIMEOUT 10000;", script);
    }

    [Fact]
    public void BuildScript_OmitsXactAbortWhenTurnedOff()
    {
        var options = new RaskSqlServerOptions { AbortOnError = false };

        Assert.Equal("SET LOCK_TIMEOUT 10000;", SqlServerSessionSettings.BuildScript(options));
    }

    [Fact]
    public void BuildScript_WithNothingToSet_IsEmpty()
    {
        var options = new RaskSqlServerOptions { AbortOnError = false, LockTimeout = TimeSpan.Zero };

        Assert.Empty(SqlServerSessionSettings.BuildScript(options));
    }

    [Fact]
    public void BuildScript_IsCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("hu-HU");
            var script = SqlServerSessionSettings.BuildScript(new RaskSqlServerOptions());

            Assert.DoesNotContain(",", script, StringComparison.Ordinal);
            Assert.Contains("SET LOCK_TIMEOUT 10000;", script, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
