using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Rask.Storage.Tests;

// #1080: StorageOptions come from Rask:Storage first and the AddRaskStorage callback second, like every other Rask area,
// and a bad value stops the host's start naming the section.
[Collection(StorageDbCollection.Name)]
public sealed class RaskStorageOptionsBindingTests
{
    [Fact]
    public void The_Rask_Storage_section_sets_the_provider_and_its_settings()
    {
        using var provider = Provider(new()
        {
            ["Rask:Storage:Provider"] = "Azure",
            ["Rask:Storage:MaxFileSize"] = "1048576",
            ["Rask:Storage:Prefix"] = "myapp/",
            ["Rask:Storage:Azure:ConnectionString"] = "UseDevelopmentStorage=true",
            ["Rask:Storage:Azure:Container"] = "files",
        });

        var options = provider.GetRequiredService<StorageOptions>();

        Assert.Equal(StorageProvider.Azure, options.Provider);
        Assert.Equal(1048576, options.MaxFileSize);
        Assert.Equal("myapp/", options.Prefix);
        Assert.Equal("files", options.Azure.Container);
    }

    [Fact]
    public void The_callback_wins_over_the_section()
    {
        using var provider = Provider(
            new() { ["Rask:Storage:MaxFileSize"] = "1048576" },
            o => o.MaxFileSize = 2048);

        Assert.Equal(2048, provider.GetRequiredService<StorageOptions>().MaxFileSize);
    }

    [Theory]
    [InlineData("Rask:Storage:Provider", "Dropbox", "Rask__Storage__Provider")]
    [InlineData("Rask:Storage:Provider", "1", "Rask__Storage__Provider")]
    [InlineData("Rask:Storage:MaxFileSize", "-5", "MaxFileSize")]
    public void An_invalid_value_fails_the_start_naming_the_section(string key, string value, string named)
    {
        using var provider = Provider(new() { [key] = value });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<StorageOptions>>().Value);

        Assert.Contains("Rask:Storage", ex.Message);
        Assert.Contains(named, ex.Message);
    }

    [Fact]
    public void A_top_level_Storage_section_is_not_read()
    {
        using var provider = Provider(new()
        {
            ["Storage:Provider"] = "S3",
            ["Storage:MaxFileSize"] = "1048576",
        });

        var options = provider.GetRequiredService<StorageOptions>();

        Assert.Equal(StorageProvider.Disk, options.Provider);
        Assert.Equal(StorageOptions.DefaultMaxFileSize, options.MaxFileSize);
    }

    // Not reading the old section is silent by nature, and an upgraded app would fall back to the defaults: a disk store
    // where it configured a bucket. The boot says so instead.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_top_level_Storage_section_is_named_as_ignored_at_start(bool present)
    {
        var log = new CapturingLoggerProvider();
        using var provider = Provider(
            present ? new() { ["Storage:Provider"] = "S3" } : new() { ["Rask:Storage:Prefix"] = "myapp/" },
            logging: log);

        // The check on its own: the model check beside it would need a real database.
        await new StorageStartupCheck(provider, provider.GetRequiredService<ILogger<StorageStartupCheck>>())
            .StartAsync(CancellationToken.None);

        var warned = log.Messages.Any(m => m.Contains("Rask:Storage", StringComparison.Ordinal)
                                           && m.Contains("no longer reads", StringComparison.Ordinal));
        Assert.Equal(present, warned);
    }

    private static ServiceProvider Provider(
        Dictionary<string, string?> settings, Action<StorageOptions>? configure = null, ILoggerProvider? logging = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (logging is not null)
            {
                builder.AddProvider(logging);
            }
        });
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddRaskStorage<StorageDbContext>(configure);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(Messages);

        public void Dispose()
        {
        }

        private sealed class Logger(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }
}
