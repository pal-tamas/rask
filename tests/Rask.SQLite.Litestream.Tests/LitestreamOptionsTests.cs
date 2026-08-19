using System.Reflection;

namespace Rask.SQLite.Litestream.Tests;

public sealed class LitestreamOptionsTests
{
    [Fact]
    public void Defaults_are_sensible()
    {
        var options = new LitestreamOptions();

        Assert.Equal("litestream", options.ExecutablePath);
        Assert.True(options.RestoreOnStartup);
        Assert.Equal(TimeSpan.FromSeconds(10), options.ShutdownGracePeriod);
        Assert.Equal(TimeSpan.FromSeconds(5), options.RestartDelay);
    }

    [Fact]
    public void Validate_rejects_grace_period_over_int_max_milliseconds()
    {
        var options = new LitestreamOptions
        {
            DatabasePath = "/data/app.db",
            ReplicaUrl = "s3://bucket/app",
            ShutdownGracePeriod = TimeSpan.FromDays(30),
        };
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_rejects_negative_restart_delay()
    {
        var options = new LitestreamOptions
        {
            DatabasePath = "/data/app.db",
            ReplicaUrl = "s3://bucket/app",
            RestartDelay = TimeSpan.FromSeconds(-1),
        };
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_accepts_url_form()
    {
        var options = new LitestreamOptions { DatabasePath = "/data/app.db", ReplicaUrl = "s3://bucket/app" };
        Validate(options); // does not throw
    }

    [Fact]
    public void Validate_accepts_config_form_without_url()
    {
        var options = new LitestreamOptions { ConfigPath = "/etc/litestream.yml" };
        Validate(options); // does not throw
    }

    [Fact]
    public void Validate_requires_database_path_in_url_form()
    {
        var options = new LitestreamOptions { ReplicaUrl = "s3://bucket/app" };
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_requires_replica_url_in_url_form()
    {
        var options = new LitestreamOptions { DatabasePath = "/data/app.db" };
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_rejects_empty_executable_path()
    {
        var options = new LitestreamOptions
        {
            ExecutablePath = "",
            DatabasePath = "/data/app.db",
            ReplicaUrl = "s3://bucket/app",
        };
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_rejects_negative_grace_period()
    {
        var options = new LitestreamOptions
        {
            DatabasePath = "/data/app.db",
            ReplicaUrl = "s3://bucket/app",
            ShutdownGracePeriod = TimeSpan.FromSeconds(-1),
        };
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_rejects_a_non_positive_verification_interval()
    {
        var options = Valid();
        options.Verification.Interval = TimeSpan.Zero;
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_rejects_a_non_positive_verification_poll_interval()
    {
        var options = Valid();
        options.Verification.PollInterval = TimeSpan.Zero;
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_rejects_a_grace_period_that_swallows_the_whole_budget()
    {
        var options = Valid();
        options.Verification.ReplicationGrace = TimeSpan.FromMinutes(5);
        options.Verification.Timeout = TimeSpan.FromMinutes(2);

        // Otherwise no restore attempt fits inside the budget and every pass is silently inconclusive —
        // a verification job that never verifies is worse than none at all.
        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Verification_is_off_by_default()
    {
        // Every pass is a real restore and a real egress bill; opting in is the user's call.
        Assert.False(new LitestreamOptions().Verification.Enabled);
    }

    private static LitestreamOptions Valid() =>
        new() { DatabasePath = "/data/app.db", ReplicaUrl = "s3://bucket/app" };

    private static void Validate(LitestreamOptions options)
    {
        var method = typeof(LitestreamOptions).GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
