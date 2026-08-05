using Microsoft.Data.Sqlite;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// <c>rask db backup</c> / <c>restore</c>: the argv handed to docker, where the local database is found,
/// and the round trip through SQLite's Online Backup API.
/// </summary>
/// <remarks>
/// The argv builders are pure and tested directly, the way <c>BuildEfArguments</c> is — a docker command
/// line that touches a production volume is worth pinning exactly rather than inferring from behaviour.
/// </remarks>
public sealed class DbBackupTests
{
    [Fact]
    public void The_vacuum_runs_in_a_throwaway_container_on_the_apps_data_volume()
    {
        var args = DbCommand.BuildRemoteVacuumArguments("deploy@example.com", "shop");

        Assert.Equal(["-H", "ssh://deploy@example.com", "run", "--rm", "-v", "shop-data:/data"], args.Take(6));
        Assert.Contains("--rm", args); // nothing is left running on the host
        Assert.Contains(args, a => a.Contains("VACUUM INTO", StringComparison.Ordinal));
        Assert.Contains(args, a => a.Contains("/data/app.db", StringComparison.Ordinal));
    }

    [Fact]
    public void The_vacuum_clears_a_previous_runs_staged_file_first()
    {
        // A run that died between the vacuum and its cleanup leaves the output behind, and VACUUM INTO
        // refuses to overwrite — so every later backup would fail until someone cleaned up by hand.
        var script = DbCommand.BuildRemoteVacuumArguments("h", "shop").Last();

        Assert.StartsWith("rm -f /data/.rask-backup.db &&", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_replace_removes_the_wal_sidecars_before_moving_the_file_in()
    {
        // Leaving a stale -wal beside a replaced database is how a restore silently yields a hybrid of the
        // two: SQLite replays the old log over the new file.
        var script = DbCommand.BuildRemoteReplaceArguments("h", "shop").Last();

        Assert.Contains("rm -f /data/app.db-wal /data/app.db-shm", script, StringComparison.Ordinal);
        Assert.Contains("mv /data/.rask-backup.db /data/app.db", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("rm -f", StringComparison.Ordinal) < script.IndexOf("mv ", StringComparison.Ordinal),
            "the sidecars must go before the file is moved in");
    }

    [Fact]
    public void Copy_down_and_up_address_the_helper_container_not_the_volume()
    {
        // `docker cp` cannot address a volume, which is why the helper container exists at all.
        Assert.Equal(
            ["-H", "ssh://h", "cp", "helper:/data/.rask-backup.db", "/tmp/out.db"],
            DbCommand.BuildCopyDownArguments("h", "helper", "/tmp/out.db"));

        Assert.Equal(
            ["-H", "ssh://h", "cp", "/tmp/in.db", "helper:/data/.rask-backup.db"],
            DbCommand.BuildCopyUpArguments("h", "helper", "/tmp/in.db"));
    }

    [Fact]
    public void The_helper_container_is_created_but_never_started()
    {
        var args = DbCommand.BuildHelperCreateArguments("h", "shop", "helper");

        Assert.Contains("create", args);
        Assert.DoesNotContain("run", args);
        Assert.Contains("shop-data:/data", args);
    }

    [Fact]
    public void Every_remote_argument_list_targets_the_host_over_ssh()
    {
        // -H ssh://… is the whole reason none of this needs anything installed on the host.
        var lists = new[]
        {
            DbCommand.BuildRemoteVacuumArguments("h", "s"),
            DbCommand.BuildHelperCreateArguments("h", "s", "c"),
            DbCommand.BuildHelperRemoveArguments("h", "c"),
            DbCommand.BuildCopyDownArguments("h", "c", "x"),
            DbCommand.BuildCopyUpArguments("h", "c", "x"),
            DbCommand.BuildRemoteCleanupArguments("h", "s"),
            DbCommand.BuildRemoteReplaceArguments("h", "s"),
            DbCommand.BuildStopArguments("h", "s"),
            DbCommand.BuildStartArguments("h", "s"),
        };

        foreach (var args in lists)
        {
            Assert.Equal("-H", args[0]);
            Assert.Equal("ssh://h", args[1]);
        }
    }

    [Fact]
    public void The_default_backup_name_sorts_chronologically_and_never_collides()
    {
        var first = DbCommand.DefaultBackupName("shop", new DateTimeOffset(2026, 8, 5, 8, 15, 0, TimeSpan.Zero));
        var second = DbCommand.DefaultBackupName("shop", new DateTimeOffset(2026, 8, 5, 9, 15, 0, TimeSpan.Zero));

        Assert.Equal("shop-20260805-081500.db", first);
        Assert.True(string.CompareOrdinal(first, second) < 0);
    }

    [Fact]
    public void The_default_backup_name_is_stamped_in_utc()
    {
        // A local-time stamp makes backups from two machines interleave wrongly, and go backwards over a
        // DST boundary.
        var name = DbCommand.DefaultBackupName("shop", new DateTimeOffset(2026, 8, 5, 10, 15, 0, TimeSpan.FromHours(2)));

        Assert.Equal("shop-20260805-081500.db", name);
    }

    [Theory]
    [InlineData("Data Source=app.db", "app.db")]
    [InlineData("DataSource=app.db;Cache=Shared", "app.db")]
    [InlineData("Filename=/var/data/app.db", "/var/data/app.db")]
    [InlineData("Cache=Shared;Data Source=nested/app.db;Pooling=False", "nested/app.db")]
    [InlineData("Mode=ReadOnly", null)]
    public void The_data_source_is_read_out_of_the_connection_string(string connectionString, string? expected)
    {
        Assert.Equal(expected, SqliteDatabaseLocator.DataSourceOf(connectionString));
    }

    [Fact]
    public void The_database_is_found_from_the_apps_own_connection_string()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/appsettings.json", """{"ConnectionStrings":{"App":"Data Source=data/shop.db"}}""");

        var (path, error) = SqliteDatabaseLocator.Locate(fs, "/app");

        Assert.Null(error);
        Assert.Equal(Path.GetFullPath(Path.Combine("/app", "data/shop.db")), path);
    }

    [Fact]
    public void An_environment_override_wins_over_the_base_settings()
    {
        // Configuration's own precedence: the Development override is the database you have locally, and
        // the one you mean when you ask for a backup.
        var fs = new FakeFileSystem();
        fs.Seed("/app/appsettings.json", """{"ConnectionStrings":{"App":"Data Source=prod.db"}}""");
        fs.Seed("/app/appsettings.Development.json", """{"ConnectionStrings":{"App":"Data Source=dev.db"}}""");

        var (path, _) = SqliteDatabaseLocator.Locate(fs, "/app");

        Assert.EndsWith("dev.db", path!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_with_no_configured_string_falls_back_to_the_scaffolded_default()
    {
        // The generated Program.cs reads `GetConnectionString("App") ?? "Data Source=app.db"`, so an app
        // that never configured one still has a database — and it must still be backed up.
        var (path, error) = SqliteDatabaseLocator.Locate(new FakeFileSystem(), "/app");

        Assert.Null(error);
        Assert.EndsWith("app.db", path!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_file_data_source_is_refused_rather_than_guessed_at()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/appsettings.json", """{"ConnectionStrings":{"App":"Data Source=:memory:"}}""");

        var (path, error) = SqliteDatabaseLocator.Locate(fs, "/app");

        Assert.Null(path);
        Assert.Contains("nothing to copy", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_corrupt_settings_file_does_not_wedge_the_command()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/appsettings.json", "{ not json");

        var (path, error) = SqliteDatabaseLocator.Locate(fs, "/app");

        Assert.Null(error);
        Assert.EndsWith("app.db", path!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_backup_taken_through_the_online_api_is_a_complete_readable_database()
    {
        // The claim that matters: the copy is one self-contained file with the committed rows in it, taken
        // while WAL is on — which a plain file copy of the .db alone would not give.
        var directory = Path.Combine(Path.GetTempPath(), $"rask-db-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "app.db");
        var destination = Path.Combine(directory, "backup.db");

        try
        {
            using (var connection = new SqliteConnection($"Data Source={source}"))
            {
                connection.Open();
                Exec(connection, "PRAGMA journal_mode=WAL;");
                Exec(connection, "CREATE TABLE t(v TEXT NOT NULL);");
                Exec(connection, "INSERT INTO t(v) VALUES('committed');");

                using var to = new SqliteConnection($"Data Source={destination}");
                to.Open();
                connection.BackupDatabase(to);
            }

            // Read the copy with no sidecars present at all — proof the WAL was folded in.
            Assert.False(File.Exists(destination + "-wal"));
            using var reader = new SqliteConnection($"Data Source={destination};Mode=ReadOnly");
            reader.Open();
            using var command = reader.CreateCommand();
            command.CommandText = "SELECT v FROM t;";
            Assert.Equal("committed", command.ExecuteScalar());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
