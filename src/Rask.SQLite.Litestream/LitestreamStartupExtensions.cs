using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Litestream;

/// <summary>Startup helpers for restoring a SQLite database from its Litestream replica.</summary>
public static class LitestreamStartupExtensions
{
    /// <summary>
    /// Restores the SQLite database from its Litestream replica if
    /// <see cref="LitestreamOptions.RestoreOnStartup"/> is set and the local file is missing (a fresh
    /// container/host). Call this after <c>Build()</c> and <b>before</b> the app opens the database
    /// (schema creation, migrations, first query). No-op — and never a clobber — when the database
    /// already exists locally. Returns <see langword="true"/> if a restore was attempted.
    /// </summary>
    public static async Task<bool> RestoreSqliteFromLitestreamAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var restorer = services.GetService<LitestreamRestorer>()
            ?? throw new InvalidOperationException(
                "Call AddRaskSqliteLitestream(...) before RestoreSqliteFromLitestreamAsync().");

        return await restorer.RestoreAsync(cancellationToken).ConfigureAwait(false);
    }
}
