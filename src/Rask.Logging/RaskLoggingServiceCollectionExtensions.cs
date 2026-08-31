using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Rask.Logging;

/// <summary>Registers the durable log store into an <see cref="IServiceCollection"/>.</summary>
public static class RaskLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Captures the application's log into a SQLite database of its own at
    /// <paramref name="connectionString"/>, so what happened survives the restart that hid it.
    /// <code>
    /// builder.Services.AddRaskLogging(
    ///     builder.Configuration.GetConnectionString("Logs") ?? "Data Source=logs.db");
    /// </code>
    /// <para>
    /// Takes a connection string rather than a <c>TContext</c> like the other database-backed pillars: the
    /// store deliberately owns its own file. See <see cref="ILogs"/> for why, and remember that the
    /// file is <b>not</b> covered by <c>rask db backup</c> or Litestream.
    /// </para>
    /// <para>
    /// The schema is created on first use — there is no migration to add. Entries below
    /// <see cref="RaskLoggingOptions.MinimumLevel"/> are skipped, and so is anything your
    /// <c>Logging:LogLevel</c> configuration already filtered, since that runs first.
    /// </para>
    /// </summary>
    public static IServiceCollection AddRaskLogging(
        this IServiceCollection services,
        string connectionString,
        Action<RaskLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var options = new RaskLoggingOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<LogMetrics>();
        services.TryAddSingleton<LogChannel>();
        services.TryAddSingleton<ILogs>(sp => new SqliteLogStore(
            connectionString,
            sp.GetRequiredService<RaskLoggingOptions>(),
            sp.GetRequiredService<TimeProvider>()));

        // Registered as a logging provider rather than a bespoke channel, so the store sees exactly what
        // every other sink sees. TryAddEnumerable keys on the implementation type, so a repeated
        // AddRaskLogging call doesn't double-capture every entry.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, RaskLoggerProvider>());
        services.AddHostedService<LogWriter>();

        return services;
    }
}
