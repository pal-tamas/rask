using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Rask.SQLite.Litestream.Tests;

/// <summary>
/// Drives the whole verification round trip against a real SQLite file, with a fake executor standing in
/// for the replica: "restore" copies whichever bytes the test says the replica holds into the requested
/// <c>-o</c> path. That covers the part that matters — did the sentinel survive the trip — without the
/// binary, a bucket, or a network.
/// </summary>
public sealed class LitestreamVerifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rask-litestream-verify-tests-{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly string _tempDirectory;

    public LitestreamVerifierTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "app.db");
        _tempDirectory = Path.Combine(_root, "temp");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task VerifyAsync_reports_verified_when_the_sentinel_survives_the_round_trip()
    {
        CreateDatabase();
        var status = new LitestreamStatus();
        // A replica that is genuinely current: every restore hands back the live file as it stands now.
        var verifier = NewVerifier(new FakeReplica(_dbPath, live: true), status, Options());

        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.Equal(LitestreamVerificationOutcome.Verified, result.Outcome);
        Assert.NotNull(result.LastVerifiedAt);
        Assert.Null(result.LastError);
        Assert.Equal(result, status.Verification);
    }

    [Fact]
    public async Task VerifyAsync_restores_elsewhere_and_never_passes_if_replica_exists()
    {
        CreateDatabase();
        var replica = new FakeReplica(_dbPath, live: true);
        var verifier = NewVerifier(replica, new LitestreamStatus(), Options());

        await verifier.VerifyAsync(CancellationToken.None);

        var arguments = Assert.Single(replica.Invocations);
        // -if-replica-exists would turn "there is no replica at all" into a silent pass, which is the exact
        // failure verification exists to catch.
        Assert.DoesNotContain("-if-replica-exists", arguments);
        var output = OutputPath(arguments);
        Assert.StartsWith(_tempDirectory, output, StringComparison.Ordinal);
        // Never beside the live database, where a stray -wal/-shm could be mistaken for the real thing.
        Assert.NotEqual(Path.GetDirectoryName(_dbPath), Path.GetDirectoryName(output));
    }

    [Fact]
    public async Task VerifyAsync_is_inconclusive_when_the_sentinel_has_not_shipped_yet()
    {
        CreateDatabase();
        var status = new LitestreamStatus();
        // A replica frozen before the pass began: the restore works, the sentinel simply isn't in it.
        var verifier = NewVerifier(new FakeReplica(_dbPath, live: false), status, Options(budget: TimeSpan.FromMilliseconds(200)));

        var result = await verifier.VerifyAsync(CancellationToken.None);

        // Lag is not a broken backup: a job that pages for this is a job that gets switched off.
        Assert.Equal(LitestreamVerificationOutcome.Inconclusive, result.Outcome);
        Assert.Null(result.LastVerifiedAt);
        Assert.NotNull(result.LastError);
    }

    [Fact]
    public async Task VerifyAsync_fails_when_the_restore_itself_fails()
    {
        CreateDatabase();
        var verifier = NewVerifier(new FakeReplica(_dbPath, live: true) { ExitCode = 1 }, new LitestreamStatus(), Options());

        var result = await verifier.VerifyAsync(CancellationToken.None);

        // Wrong prefix, rotated credentials, empty bucket — all of these keep IsReplicating true.
        Assert.Equal(LitestreamVerificationOutcome.Failed, result.Outcome);
        Assert.Contains("exit code 1", result.LastError);
    }

    [Fact]
    public async Task VerifyAsync_fails_when_the_restore_claims_success_but_writes_nothing()
    {
        CreateDatabase();
        var verifier = NewVerifier(new FakeReplica(_dbPath, live: true) { WriteOutput = false }, new LitestreamStatus(), Options());

        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.Equal(LitestreamVerificationOutcome.Failed, result.Outcome);
        Assert.Contains("produced no database", result.LastError);
    }

    [Fact]
    public async Task VerifyAsync_treats_a_replica_without_the_probe_table_as_not_yet_shipped()
    {
        CreateDatabase();
        // The first ever pass restores a replica that predates the probe table. "No such table" is an
        // ordinary not-yet answer, not an exception to fail the backup over.
        var empty = Path.Combine(_root, "empty.db");
        CreateDatabase(empty);
        var verifier = NewVerifier(
            new FakeReplica(empty, live: true),
            new LitestreamStatus(),
            Options(budget: TimeSpan.FromMilliseconds(200)));

        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.Equal(LitestreamVerificationOutcome.Inconclusive, result.Outcome);
    }

    [Fact]
    public async Task VerifyAsync_skips_in_config_mode_without_a_database_path()
    {
        var options = new LitestreamOptions { ConfigPath = "/etc/litestream.yml" };
        options.Verification.TempDirectory = _tempDirectory;
        var replica = new FakeReplica(_dbPath, live: true);
        var verifier = NewVerifier(replica, new LitestreamStatus(), options);

        var result = await verifier.VerifyAsync(CancellationToken.None);

        // Same ambiguity RestoreAsync declines to guess at, answered the same way.
        Assert.Equal(LitestreamVerificationOutcome.Skipped, result.Outcome);
        Assert.Empty(replica.Invocations);
    }

    [Fact]
    public async Task VerifyAsync_skips_when_the_database_does_not_exist_yet()
    {
        var replica = new FakeReplica(_dbPath, live: true);
        var verifier = NewVerifier(replica, new LitestreamStatus(), Options());

        var result = await verifier.VerifyAsync(CancellationToken.None);

        Assert.Equal(LitestreamVerificationOutcome.Skipped, result.Outcome);
        Assert.Empty(replica.Invocations);
    }

    [Fact]
    public async Task VerifyAsync_deletes_its_temporary_directory_on_every_path()
    {
        CreateDatabase();

        foreach (var replica in new[]
                 {
                     new FakeReplica(_dbPath, live: true),
                     new FakeReplica(_dbPath, live: false),
                     new FakeReplica(_dbPath, live: true) { ExitCode = 1 },
                 })
        {
            await NewVerifier(replica, new LitestreamStatus(), Options(budget: TimeSpan.FromMilliseconds(100)))
                .VerifyAsync(CancellationToken.None);
        }

        // A restored copy left behind is a full database on disk, every pass, forever.
        Assert.Empty(Directory.GetFileSystemEntries(_tempDirectory));
    }

    [Fact]
    public async Task VerifyAsync_keeps_the_last_verified_time_across_a_later_inconclusive_pass()
    {
        CreateDatabase();
        var status = new LitestreamStatus();

        var verified = await NewVerifier(new FakeReplica(_dbPath, live: true), status, Options())
            .VerifyAsync(CancellationToken.None);
        var later = await NewVerifier(
                new FakeReplica(_dbPath, live: false),
                status,
                Options(budget: TimeSpan.FromMilliseconds(100)))
            .VerifyAsync(CancellationToken.None);

        // The age of the last proven round trip is the thing worth alerting on, so it has to survive a
        // pass that merely raced replication.
        Assert.Equal(LitestreamVerificationOutcome.Inconclusive, later.Outcome);
        Assert.Equal(verified.LastVerifiedAt, later.LastVerifiedAt);
        Assert.True(later.LastAttemptedAt >= verified.LastAttemptedAt);
    }

    [Fact]
    public async Task VerifyAsync_writes_exactly_one_probe_row_however_often_it_runs()
    {
        CreateDatabase();
        var verifier = NewVerifier(new FakeReplica(_dbPath, live: true), new LitestreamStatus(), Options());

        await verifier.VerifyAsync(CancellationToken.None);
        await verifier.VerifyAsync(CancellationToken.None);
        await verifier.VerifyAsync(CancellationToken.None);

        // A database probed daily for a year must be one row heavier at the end of it, not 365.
        await using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        await connection.OpenAsync(CancellationToken.None);
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM __rask_backup_probe";
        Assert.Equal(1L, await count.ExecuteScalarAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private LitestreamOptions Options(TimeSpan? budget = null)
    {
        var options = new LitestreamOptions { DatabasePath = _dbPath, ReplicaUrl = "s3://bucket/app" };
        options.Verification.TempDirectory = _tempDirectory;
        options.Verification.ReplicationGrace = TimeSpan.Zero;
        options.Verification.PollInterval = TimeSpan.FromMilliseconds(10);
        options.Verification.Timeout = budget ?? TimeSpan.FromSeconds(30);
        return options;
    }

    // Where the restore was told to write, read back out of the argument list the builder produced.
    private static string OutputPath(IReadOnlyList<string> arguments)
    {
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] == "-o")
            {
                return arguments[i + 1];
            }
        }

        throw new InvalidOperationException("The restore arguments carry no -o output path.");
    }

    private static LitestreamVerifier NewVerifier(FakeReplica replica, LitestreamStatus status, LitestreamOptions options) =>
        new(options, replica, status, TimeProvider.System, NullLogger<LitestreamVerifier>.Instance);

    private void CreateDatabase(string? path = null)
    {
        using var connection = new SqliteConnection($"Data Source={path ?? _dbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS orders (id INTEGER PRIMARY KEY)";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Stands in for the replica. <c>live: true</c> means a replica that is fully caught up — the restore
    /// hands back the source exactly as it is at that moment. <c>live: false</c> freezes a copy at
    /// construction, which is what "the sentinel hasn't shipped yet" looks like from the outside.
    /// </summary>
    private sealed class FakeReplica : ILitestreamExecutor
    {
        private readonly string _sourcePath;
        private readonly byte[]? _frozen;

        public FakeReplica(string sourcePath, bool live)
        {
            _sourcePath = sourcePath;
            _frozen = live || !File.Exists(sourcePath) ? null : File.ReadAllBytes(sourcePath);
        }

        public int ExitCode { get; init; }

        public bool WriteOutput { get; init; } = true;

        public List<IReadOnlyList<string>> Invocations { get; } = [];

        public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Invocations.Add(arguments);

            if (ExitCode == 0 && WriteOutput)
            {
                var output = OutputPath(arguments);
                var bytes = _frozen ?? await File.ReadAllBytesAsync(_sourcePath, cancellationToken);
                await File.WriteAllBytesAsync(output, bytes, cancellationToken);
            }

            return ExitCode;
        }
    }
}
