using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.SQLite.Litestream;

/// <summary>Registers the managed Litestream supervisor into an <see cref="IServiceCollection"/>.</summary>
public static class LitestreamServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Litestream restorer and the background replication service. Configure at least a
    /// <see cref="LitestreamOptions.DatabasePath"/> + <see cref="LitestreamOptions.ReplicaUrl"/> (or a
    /// <see cref="LitestreamOptions.ConfigPath"/>). Call
    /// <see cref="LitestreamStartupExtensions.RestoreSqliteFromLitestreamAsync"/> after
    /// <c>Build()</c> and before opening the database to restore on a fresh host. Idempotent.
    /// <para>
    /// Also registers <see cref="LitestreamStatus"/>, a singleton reporting whether replication is currently
    /// running and how often it has restarted.
    /// </para>
    /// </summary>
    public static IServiceCollection AddRaskSqliteLitestream(
        this IServiceCollection services,
        Action<LitestreamOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // Idempotent: a second call is a no-op so the replication service isn't registered twice.
        if (services.Any(static d => d.ServiceType == typeof(LitestreamMarker)))
        {
            return services;
        }

        services.AddSingleton(new LitestreamMarker());

        var options = new LitestreamOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<LitestreamStatus>();
        services.TryAddSingleton<ILitestreamExecutor, CliWrapLitestreamExecutor>();
        services.TryAddSingleton<LitestreamRestorer>();
        services.AddHostedService<LitestreamReplicationService>();

        return services;
    }

    // Sentinel marking that AddRaskSqliteLitestream already ran on this collection.
    private sealed class LitestreamMarker;
}
