using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Rask.SQLite.Litestream;

/// <summary>
/// The default <see cref="ISqliteBackupVerifier"/>: a sentinel round trip through the real replica.
/// <para>
/// Runs entirely through <see cref="ILitestreamExecutor"/>, so the whole pass is unit-testable with a
/// fake and no <c>litestream</c> binary.
/// </para>
/// </summary>
public sealed class LitestreamVerifier : ISqliteBackupVerifier
{
    // One row, upserted in place, so a database being probed daily for a year is one row heavier at the
    // end of it. The name is namespaced to make it obvious in a schema dump who put it there.
    private const string ProbeTable = "__rask_backup_probe";

    private const string CreateProbeSql = """
        CREATE TABLE IF NOT EXISTS __rask_backup_probe (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            token TEXT NOT NULL,
            written_at TEXT NOT NULL
        )
        """;

    private const string UpsertProbeSql = """
        INSERT INTO __rask_backup_probe (id, token, written_at) VALUES (1, $token, $written_at)
        ON CONFLICT (id) DO UPDATE SET token = excluded.token, written_at = excluded.written_at
        """;

    private const string ProbeTableExistsSql =
        "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $table";

    private const string SelectProbeSql = "SELECT token FROM __rask_backup_probe WHERE id = 1";

    private readonly LitestreamOptions _options;
    private readonly ILitestreamExecutor _executor;
    private readonly LitestreamStatus _status;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LitestreamVerifier> _logger;

    /// <summary>Creates a verifier over the configured options, executor and status.</summary>
    public LitestreamVerifier(
        LitestreamOptions options,
        ILitestreamExecutor executor,
        LitestreamStatus status,
        TimeProvider timeProvider,
        ILogger<LitestreamVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _executor = executor;
        _status = status;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<LitestreamVerificationStatus> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var verification = _options.Verification;
        var databasePath = _options.DatabasePath;

        if (string.IsNullOrWhiteSpace(databasePath))
        {
            // Same ambiguity RestoreAsync already declines to guess at: a litestream.yml can manage several
            // databases, so there is no single one to write a sentinel into. Behave consistently rather
            // than picking one.
            return Skip("set DatabasePath to verify (config-mode multi-database verification is not automatic).");
        }

        if (!File.Exists(databasePath))
        {
            return Skip($"{databasePath} does not exist yet, so there is nothing to probe.");
        }

        var token = Guid.NewGuid().ToString("N");
        var startedAt = _timeProvider.GetTimestamp();
        string? temporaryDirectory = null;

        try
        {
            await WriteSentinelAsync(databasePath, token, verification, cancellationToken).ConfigureAwait(false);

            temporaryDirectory = CreateTemporaryDirectory(verification);
            var restoredPath = Path.Combine(temporaryDirectory, "verify.db");

            // Wait before the first attempt rather than after a failed one: replication normally ships the
            // sentinel in seconds, so this makes the common case cost exactly one restore.
            await Task.Delay(verification.ReplicationGrace, _timeProvider, cancellationToken).ConfigureAwait(false);

            while (true)
            {
                // litestream refuses to restore over an existing output file, so each attempt starts clean.
                DeleteRestored(restoredPath);

                var arguments = LitestreamCommand.Restore(_options, restoredPath, ifReplicaExists: false);
                var exitCode = await _executor.RunAsync(arguments, cancellationToken).ConfigureAwait(false);
                if (exitCode != 0)
                {
                    // Not lag — the replica could not be read at all. Wrong prefix, rotated credentials,
                    // an empty bucket: the failures that leave IsReplicating cheerfully true.
                    return Fail($"litestream restore failed with exit code {exitCode}.");
                }

                if (!File.Exists(restoredPath))
                {
                    return Fail("litestream restore reported success but produced no database.");
                }

                if (await ReadSentinelAsync(restoredPath, cancellationToken).ConfigureAwait(false) == token)
                {
                    var lag = _timeProvider.GetElapsedTime(startedAt);
                    _logger.LogInformation(
                        "Litestream backup verified restorable; the sentinel round-tripped in {Lag}.", lag);
                    return _status.MarkVerified(_timeProvider.GetUtcNow(), lag);
                }

                // The restore worked and the sentinel is not in it yet. That is lag until the budget runs
                // out, and lag is not a broken backup.
                if (_timeProvider.GetElapsedTime(startedAt) + verification.PollInterval >= verification.Timeout)
                {
                    return Inconclusive(
                        $"the sentinel had not reached the replica within {verification.Timeout}; "
                        + "replication may be lagging. Raise Verification.Timeout if this repeats.");
                }

                await Task.Delay(verification.PollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down mid-pass. Not a verdict about the backup, so publish nothing.
            throw;
        }
#pragma warning disable CA1031 // A backup check must never crash the app it protects — report and carry on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return Fail(ex.Message);
        }
        finally
        {
            if (temporaryDirectory is not null)
            {
                DeleteTemporaryDirectory(temporaryDirectory);
            }
        }
    }

    private async Task WriteSentinelAsync(
        string databasePath,
        string token,
        LitestreamVerificationOptions verification,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString(databasePath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // The sentinel takes the write lock on the live database, so it goes through the non-blocking
        // fair-interval retry: it waits out a busy writer by yielding the thread, never by holding one.
        // The work is a fixed-token upsert, so it stays correct if a contended COMMIT makes it re-run.
        await connection.InImmediateTransactionAsync(
            verification.BusyRetry,
            async (writable, ct) =>
            {
                await using (var create = writable.CreateCommand())
                {
                    create.CommandText = CreateProbeSql;
                    await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using var upsert = writable.CreateCommand();
                upsert.CommandText = UpsertProbeSql;
                upsert.Parameters.AddWithValue("$token", token);
                upsert.Parameters.AddWithValue("$written_at", _timeProvider.GetUtcNow().ToString("O"));
                await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            _timeProvider,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadSentinelAsync(string restoredPath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString(restoredPath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Ask the schema first: on the first ever pass the replica predates the probe table, and "no such
        // table" is an ordinary not-yet-shipped answer rather than something to raise an exception over.
        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = ProbeTableExistsSql;
            exists.Parameters.AddWithValue("$table", ProbeTable);
            if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                return null;
            }
        }

        await using var read = connection.CreateCommand();
        read.CommandText = SelectProbeSql;
        return await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    // Pooling off on both connections: these are short-lived probes, and a pooled handle kept open past
    // Dispose is exactly what stops the temp file being deleted afterwards.
    private static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

    private static string CreateTemporaryDirectory(LitestreamVerificationOptions verification)
    {
        var directory = Path.Combine(
            verification.TempDirectory ?? Path.GetTempPath(),
            $"rask-litestream-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteRestored(string restoredPath)
    {
        foreach (var path in new[] { restoredPath, restoredPath + "-wal", restoredPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private void DeleteTemporaryDirectory(string directory)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
#pragma warning disable CA1031 // Failing to tidy a temp directory must not turn a good verification into a bad one.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Worth a line — a temp directory left behind every day adds up — but never worth failing over.
            _logger.LogWarning(ex, "Could not delete the Litestream verification directory {Directory}.", directory);
        }
    }

    private LitestreamVerificationStatus Skip(string reason)
    {
        _logger.LogInformation("Litestream backup verification skipped: {Reason}", reason);
        return _status.MarkVerification(LitestreamVerificationOutcome.Skipped, _timeProvider.GetUtcNow(), reason);
    }

    private LitestreamVerificationStatus Inconclusive(string reason)
    {
        _logger.LogWarning("Litestream backup verification inconclusive: {Reason}", reason);
        return _status.MarkVerification(LitestreamVerificationOutcome.Inconclusive, _timeProvider.GetUtcNow(), reason);
    }

    private LitestreamVerificationStatus Fail(string error)
    {
        // Critical, like a replication failure: this is the state where the backup exists but cannot be
        // restored, which is the one that is only ever discovered at the worst possible moment.
        _logger.LogCritical("Litestream backup is NOT restorable: {Error}", error);
        return _status.MarkVerification(LitestreamVerificationOutcome.Failed, _timeProvider.GetUtcNow(), error);
    }
}
