using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Hosting.Shared;

namespace Rask.SQLite.Litestream;

/// <summary>Registers the managed Litestream supervisor into an <see cref="IServiceCollection"/>.</summary>
public static class LitestreamServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Litestream restorer and the background replication service. The options read the
    /// <c>Rask:Litestream</c> configuration section first and then <paramref name="configure"/>, so code wins; at
    /// least a <see cref="LitestreamOptions.ReplicaUrl"/> (or a <see cref="LitestreamOptions.ConfigPath"/>) is
    /// required, and <see cref="LitestreamOptions.DatabasePath"/> defaults to the file named by
    /// <c>Rask:ConnectionStrings:App</c>. Call
    /// <see cref="LitestreamStartupExtensions.RestoreSqliteFromLitestreamAsync"/> after
    /// <c>Build()</c> and before opening the database to restore on a fresh host. Idempotent.
    /// <para>
    /// Also registers <see cref="LitestreamStatus"/>, a singleton reporting whether replication is currently
    /// running and how often it has restarted.
    /// </para>
    /// </summary>
    public static IServiceCollection AddRaskSqliteLitestream(
        this IServiceCollection services,
        Action<LitestreamOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Idempotent: a second call is a no-op so the replication service isn't registered twice.
        if (!services.AddRaskOptions<LitestreamOptions>(
                "Rask:Litestream", static (section, o) => section.Bind(o), configure, static o => o.Validate()))
        {
            return services;
        }

        // The database being replicated is the app's own unless something said otherwise. PostConfigure, so a path
        // from the section or the callback wins and validation still sees the result.
        services.AddOptions<LitestreamOptions>().PostConfigure<IServiceProvider>(static (o, sp) =>
            o.DatabasePath = string.IsNullOrEmpty(o.DatabasePath) ? AppDatabasePath(sp) ?? o.DatabasePath : o.DatabasePath);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<LitestreamStatus>();
        services.TryAddSingleton<ILitestreamExecutor, CliWrapLitestreamExecutor>();
        services.TryAddSingleton<LitestreamRestorer>();
        services.AddHostedService<LitestreamReplicationService>();

        // Registered whether or not the schedule runs, so an operator endpoint can verify on demand
        // without opting into a recurring restore. The schedule itself checks Verification.Enabled when it
        // starts — it can come from Rask:Litestream:Verification, which is not readable any earlier — because
        // only the schedule spends egress on its own.
        services.TryAddSingleton<ISqliteBackupVerifier, LitestreamVerifier>();
        services.AddHostedService<LitestreamVerificationService>();

        return services;
    }

    // The file behind Rask:ConnectionStrings:App, or null when there is none. Rask.SQLite.Snapshots carries the same
    // two lines: sharing them through InternalsVisibleTo would put a second copy of the source-linked options helper
    // in sight of this assembly.
    private static string? AppDatabasePath(IServiceProvider services)
    {
        if (services.GetService<IConfiguration>()?["Rask:ConnectionStrings:App"] is not { Length: > 0 } connectionString)
        {
            return null;
        }

        try
        {
            return new SqliteConnectionStringBuilder(connectionString).DataSource;
        }
        catch (ArgumentException)
        {
            // Not a SQLite connection string — an app on another database. There is no file to derive, so validation
            // reports DatabasePath under Rask:Litestream instead of the parser's "Keyword not supported" naming neither.
            return null;
        }
    }
}
