using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.SQLite;

/// <summary>Registers Rask.SQLite's raw-ADO.NET connection factory into an <see cref="IServiceCollection"/>.</summary>
public static class SqliteServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="ISqlite"/> that opens connections for
    /// <paramref name="connectionString"/> with the production pragmas applied on every
    /// open (overridable via <paramref name="configure"/>). For Entity Framework Core use
    /// <c>UseRaskSqlite</c> on the <c>DbContextOptionsBuilder</c> instead — this is for code that uses
    /// SQLite directly. Idempotent: a second call is a no-op.
    /// </summary>
    public static IServiceCollection AddRaskSqlite(
        this IServiceCollection services,
        string connectionString,
        Action<SqlitePragmaOptions>? configure = null,
        Action<SqliteBusyRetryOptions>? configureRetry = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        // Idempotent: a second registration (e.g. a shared library and the app host both call it) is a
        // no-op, so the first call's connection string and options win consistently.
        if (services.Any(static d => d.ServiceType == typeof(RaskSqliteMarker)))
        {
            return services;
        }

        services.AddSingleton(new RaskSqliteMarker());

        var options = new SqlitePragmaOptions();
        configure?.Invoke(options);
        options.Validate();

        var retry = new SqliteBusyRetryOptions();
        configureRetry?.Invoke(retry);
        retry.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(retry);
        services.TryAddSingleton<ISqlite>(
            new RaskSqliteConnectionFactory(connectionString, options, retry));

        return services;
    }

    // Sentinel marking that AddRaskSqlite already ran on this collection.
    private sealed class RaskSqliteMarker;
}
