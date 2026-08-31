using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.SQLite;

namespace Rask.Logging.Tests;

/// <summary>
/// Registration-time validation. A bad option value has to fail at <c>AddRaskLogging</c>, where the stack
/// trace points at the line that set it — not hours later when the first flush tears the host down.
/// </summary>
public sealed class RaskLoggingOptionsTests
{
    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void RejectsInvalidOptionsAtRegistration(Action<RaskLoggingOptions> configure)
    {
        var services = new ServiceCollection();

        Assert.ThrowsAny<Exception>(() => services.AddRaskLogging("Data Source=unused.db", configure));
    }

    public static TheoryData<Action<RaskLoggingOptions>> InvalidOptions() => new()
    {
        o => o.Retention = TimeSpan.FromDays(-1),
        o => o.MaxRows = -1,
        o => o.FlushInterval = TimeSpan.Zero,
        o => o.BatchSize = 0,
        o => o.QueueCapacity = 0,
        o => o.PurgeInterval = TimeSpan.Zero,
        o => o.ShutdownDrainTimeout = TimeSpan.FromSeconds(-1),
        o => o.Pragmas.JournalMode = (SqliteJournalMode)99,
    };

    [Fact]
    public void RejectsAnEmptyConnectionString()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddRaskLogging(string.Empty));
    }

    [Fact]
    public void DefaultsBoundTheStoreByBothAgeAndRowCount()
    {
        var options = new RaskLoggingOptions();

        // Either limit alone leaves the disk unbounded — age lets a storm fill it inside the window, and a
        // row cap alone can shrink the window to minutes. The defaults set both on purpose.
        Assert.True(options.Retention > TimeSpan.Zero);
        Assert.True(options.MaxRows > 0);
    }

    /// <summary>
    /// Registering twice must not capture every entry twice. <c>TryAddEnumerable</c> keys on the
    /// implementation type, which is what makes a library and its host both calling this safe.
    /// </summary>
    [Fact]
    public void RegisteringTwiceCapturesEachEntryOnce()
    {
        var services = new ServiceCollection();
        services.AddRaskLogging("Data Source=unused.db");
        services.AddRaskLogging("Data Source=unused.db");

        Assert.Single(services, d => d.ServiceType == typeof(ILoggerProvider));
        Assert.Single(services, d => d.ServiceType == typeof(ILogs));
    }

    [Fact]
    public void ExcludesTheStoresOwnCategoriesByPrefix()
    {
        var options = new RaskLoggingOptions();

        Assert.True(options.IsExcluded("Rask.Logging"));
        Assert.True(options.IsExcluded("Rask.Logging.LogWriter"));
        Assert.True(options.IsExcluded("Microsoft.Data.Sqlite.Command"));
        Assert.False(options.IsExcluded("Rask.Live"));
        Assert.False(options.IsExcluded("App.Checkout"));
    }

    [Fact]
    public void ExcludesConfiguredPrefixes()
    {
        var options = new RaskLoggingOptions();
        options.ExcludedCategories.Add("Microsoft.AspNetCore.");

        Assert.True(options.IsExcluded("Microsoft.AspNetCore.Routing.Matcher"));
        Assert.False(options.IsExcluded("Microsoft.Extensions.Hosting"));
    }
}
