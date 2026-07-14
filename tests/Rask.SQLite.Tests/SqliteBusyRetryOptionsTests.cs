using System.Reflection;

namespace Rask.SQLite.Tests;

// Unit tests over the retry options' defaults and validation — no database.
public sealed class SqliteBusyRetryOptionsTests
{
    [Fact]
    public void Defaults_match_the_Rails_busy_handler()
    {
        var options = new SqliteBusyRetryOptions();

        Assert.Equal(TimeSpan.FromSeconds(5), options.Timeout);
        Assert.Equal(TimeSpan.FromMilliseconds(1), options.PollInterval);
    }

    [Fact]
    public void Validate_passes_for_defaults()
    {
        Assert.Null(Record.Exception(() => Validate(new SqliteBusyRetryOptions())));
    }

    [Fact]
    public void Validate_rejects_negative_timeout()
    {
        var options = new SqliteBusyRetryOptions { Timeout = TimeSpan.FromMilliseconds(-1) };

        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_rejects_non_positive_poll_interval(int milliseconds)
    {
        var options = new SqliteBusyRetryOptions { PollInterval = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Throws<InvalidOperationException>(() => Validate(options));
    }

    [Fact]
    public void Validate_allows_zero_timeout()
    {
        // A zero timeout means "try once, don't wait" — valid, unlike a zero poll interval.
        var options = new SqliteBusyRetryOptions { Timeout = TimeSpan.Zero };

        Assert.Null(Record.Exception(() => Validate(options)));
    }

    // Validate is internal (it runs inside the DI/EF entry points); exercise it directly via reflection.
    private static void Validate(SqliteBusyRetryOptions options)
    {
        var method = typeof(SqliteBusyRetryOptions).GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
