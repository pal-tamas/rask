using Microsoft.Extensions.Logging;

namespace Rask.SQLite.Litestream;

/// <summary>
/// Restores the SQLite database from its Litestream replica when the local file is missing — the
/// "recover on a fresh container/host" step. Never overwrites an existing database. Invoked via
/// <see cref="LitestreamStartupExtensions.RestoreSqliteFromLitestreamAsync"/> before the app opens the DB.
/// </summary>
public sealed class LitestreamRestorer
{
    private readonly LitestreamOptions _options;
    private readonly ILitestreamExecutor _executor;
    private readonly ILogger<LitestreamRestorer> _logger;

    /// <summary>Creates a restorer over the configured options and executor.</summary>
    public LitestreamRestorer(LitestreamOptions options, ILitestreamExecutor executor, ILogger<LitestreamRestorer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _executor = executor;
        _logger = logger;
    }

    /// <summary>
    /// Restores the database if <see cref="LitestreamOptions.RestoreOnStartup"/> is set and the local
    /// file does not already exist. Returns <see langword="true"/> if a restore was attempted.
    /// </summary>
    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.RestoreOnStartup)
        {
            return false;
        }

        var databasePath = _options.DatabasePath;
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            // Config mode (a litestream.yml can manage several databases): restore is per-database, so we
            // can't know which one to pull — and without a path the clobber guard below can't run either.
            // Skip automatic restore; the operator restores explicitly with `litestream restore`.
            _logger.LogInformation(
                "Litestream restore skipped: set DatabasePath to restore on startup (config-mode multi-database restore is not automatic).");
            return false;
        }

        // Treat a zero-byte file as absent: something may have touched the path before the replica was
        // pulled, and an empty file must not block the real restore. A populated file is never clobbered.
        if (File.Exists(databasePath) && new FileInfo(databasePath).Length > 0)
        {
            _logger.LogInformation("Litestream restore skipped: {DatabasePath} already exists.", databasePath);
            return false;
        }

        _logger.LogInformation("Restoring SQLite database from the Litestream replica…");
        var exitCode = await _executor.RunAsync(LitestreamCommand.Restore(_options), cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"litestream restore failed with exit code {exitCode}.");
        }

        return true;
    }
}
