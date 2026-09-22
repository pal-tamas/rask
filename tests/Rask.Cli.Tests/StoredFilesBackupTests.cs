using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// #1077: <c>rask db backup</c> / <c>restore</c> carry the disk provider's uploaded files with the database, and a
/// restore says when rows are left pointing at bytes that are not there.
/// </summary>
public sealed class StoredFilesBackupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rask-files-backup-{Guid.NewGuid():N}");

    public StoredFilesBackupTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    // --- the remote shell, pinned exactly ----------------------------------------------------------------

    [Fact]
    public void The_remote_backup_archives_the_files_after_the_database_copy_and_leaves_the_spool_out()
    {
        // After, not before: a save writes its bytes before its row, so every row in the copy has its bytes in the
        // archive. The other order would leave rows pointing at files saved between the two steps.
        var script = DbCommand.BuildRemoteVacuumArguments("h", "shop").Last();

        Assert.StartsWith("rm -f /data/.rask-backup.db /data/.rask-backup-files.tgz &&", script, StringComparison.Ordinal);
        Assert.Contains("tar -C /data -czf /data/.rask-backup-files.tgz --exclude=files/.tmp files", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("VACUUM INTO", StringComparison.Ordinal) < script.IndexOf("tar ", StringComparison.Ordinal),
            "the files must be archived after the database is copied");
    }

    [Fact]
    public void The_remote_restore_unpacks_the_archive_before_it_touches_the_database()
    {
        // A corrupt archive has to stop the restore while the database is still the one the app was running on.
        var script = DbCommand.BuildRemoteReplaceArguments("h", "shop", withFiles: true).Last();

        var extract = script.IndexOf("tar -C /data/.rask-restore-files -xzf /data/.rask-backup-files.tgz", StringComparison.Ordinal);
        var move = script.IndexOf("mv /data/.rask-backup.db /data/app.db", StringComparison.Ordinal);
        var swap = script.IndexOf("mv /data/.rask-restore-files/files /data/files", StringComparison.Ordinal);
        Assert.True(extract >= 0 && move > extract && swap > move, script);
    }

    [Fact]
    public void A_remote_restore_without_an_archive_leaves_the_files_alone()
    {
        var script = DbCommand.BuildRemoteReplaceArguments("h", "shop").Last();

        Assert.DoesNotContain("/data/files", script, StringComparison.Ordinal);
        Assert.DoesNotContain("tar", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_files_archive_travels_through_the_helper_and_cleanup_removes_every_staged_path()
    {
        Assert.Equal(
            ["-H", "ssh://h", "cp", "helper:/data/.rask-backup-files.tgz", "/tmp/out.files.tgz"],
            DbCommand.BuildFilesCopyDownArguments("h", "helper", "/tmp/out.files.tgz"));
        Assert.Equal(
            ["-H", "ssh://h", "cp", "/tmp/in.files.tgz", "helper:/data/.rask-backup-files.tgz"],
            DbCommand.BuildFilesCopyUpArguments("h", "helper", "/tmp/in.files.tgz"));

        var cleanup = DbCommand.BuildRemoteCleanupArguments("h", "shop");
        Assert.Contains("/data/.rask-backup.db", cleanup);
        Assert.Contains("/data/.rask-backup-files.tgz", cleanup);
        Assert.Contains("/data/.rask-restore-files", cleanup);
    }

    [Theory]
    [InlineData("/b/shop-20260805-081500.db", "/b/shop-20260805-081500.files.tgz")]
    [InlineData("/b/nightly", "/b/nightly.files.tgz")]
    [InlineData("/b/a.b.db", "/b/a.b.files.tgz")]
    public void The_archive_sits_beside_the_database_backup(string database, string archive) =>
        Assert.Equal(archive, StoredFilesArchive.PathFor(database));

    // --- where the files are -----------------------------------------------------------------------------

    [Fact]
    public void The_root_is_storage_under_the_project_by_default()
    {
        var fs = new FakeFileSystem();

        Assert.Equal(Path.GetFullPath("/app/storage"), StorageRootLocator.Locate(fs, "/app"));
    }

    [Fact]
    public void A_configured_relative_root_resolves_against_the_project()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/appsettings.json", """{ "Rask": { "Storage": { "Disk": { "Root": "uploads" } } } }""");

        Assert.Equal(Path.GetFullPath("/app/uploads"), StorageRootLocator.Locate(fs, "/app"));
    }

    [Fact]
    public void An_app_on_a_bucket_has_no_files_to_back_up()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/appsettings.json", """{ "Rask": { "Storage": { "Provider": "S3", "Disk": { "Root": "uploads" } } } }""");

        Assert.Null(StorageRootLocator.Locate(fs, "/app"));
    }

    // --- the archive --------------------------------------------------------------------------------------

    [Fact]
    public void A_restore_puts_back_exactly_what_was_archived()
    {
        var root = Path.Combine(_directory, "storage");
        Write(root, "private/ab/kept", "kept");
        Write(root, ".tmp/spool", "half a save");
        var archive = Path.Combine(_directory, "b.files.tgz");

        Assert.Equal(1, StoredFilesArchive.Create(root, archive));

        File.Delete(Path.Combine(root, "private/ab/kept"));
        Write(root, "private/zz/after", "saved after the backup");

        Assert.Equal(1, StoredFilesArchive.Restore(archive, root));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(root, "private/ab/kept")));
        Assert.False(File.Exists(Path.Combine(root, "private/zz/after")));
        Assert.False(Directory.Exists(Path.Combine(root, ".tmp")));
        Assert.Single(Directory.GetDirectories(_directory)); // no staging or previous directory left behind
    }

    [Fact]
    public void A_corrupt_archive_leaves_the_current_files_where_they_are()
    {
        var root = Path.Combine(_directory, "storage");
        Write(root, "private/ab/current", "current");
        var archive = Path.Combine(_directory, "broken.files.tgz");
        File.WriteAllText(archive, "not a gzip stream");

        Assert.ThrowsAny<Exception>(() => StoredFilesArchive.Restore(archive, root));

        Assert.Equal("current", File.ReadAllText(Path.Combine(root, "private/ab/current")));
        Assert.Single(Directory.GetDirectories(_directory));
    }

    [Fact]
    public void An_archive_entry_that_climbs_out_of_the_root_is_refused()
    {
        var root = Path.Combine(_directory, "storage");
        Directory.CreateDirectory(root);
        var archive = Path.Combine(_directory, "evil.files.tgz");
        using (var file = File.Create(archive))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "../escaped") { DataStream = new MemoryStream([1]) };
            tar.WriteEntry(entry);
        }

        Assert.ThrowsAny<IOException>(() => StoredFilesArchive.Restore(archive, root));

        Assert.False(File.Exists(Path.Combine(_directory, "escaped")));
    }

    // --- rows without bytes ------------------------------------------------------------------------------

    [Fact]
    public void Rows_on_disk_without_their_bytes_are_found_and_other_providers_are_ignored()
    {
        var root = Path.Combine(_directory, "storage");
        Write(root, "private/ab/present", "x");
        var db = CreateDatabase("Files", ("private/ab/present", "Disk"), ("private/cd/gone", "Disk"), ("private/ef/s3", "S3"));

        var found = StoredFilesCheck.FindMissing(db, root);

        Assert.NotNull(found);
        Assert.Equal(2, found.Checked);
        Assert.Equal(1, found.Missing);
        Assert.Equal(["private/cd/gone"], found.Examples);
    }

    [Fact]
    public void A_database_with_no_stored_file_table_is_not_checked()
    {
        var db = Path.Combine(_directory, "plain.db");
        using (var connection = new SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            Exec(connection, "CREATE TABLE Products(Id INTEGER PRIMARY KEY);");
        }

        Assert.Null(StoredFilesCheck.FindMissing(db, _directory));
    }

    // --- the command, end to end on this machine ---------------------------------------------------------

    [Fact]
    public async Task A_local_backup_and_restore_round_trips_the_database_and_its_files()
    {
        var project = CreateProject();
        File.WriteAllText(Path.Combine(project, "appsettings.json"),
            """{ "Rask": { "ConnectionStrings": { "App": "Data Source=app.db" } } }""");
        var root = Path.Combine(project, "storage");
        Write(root, "private/ab/avatar", "original bytes");
        CreateDatabase("StoredFile", Path.Combine(project, "app.db"), ("private/ab/avatar", "Disk"));
        var backup = Path.Combine(_directory, "backups", "app.db");
        var console = new StringConsole();
        var command = new DbCommand(console, new SystemFileSystem(), new FakeProcessRunner(), project);

        var backedUp = await command.ExecuteAsync(["backup", "--output", backup], CancellationToken.None);

        Assert.True(backedUp == 0, console.OutText + console.ErrorText);
        Assert.True(File.Exists(StoredFilesArchive.PathFor(backup)), console.OutText);

        // Lose the file and gain a stray one, as a box rebuilt from the database alone would.
        Directory.Delete(root, recursive: true);
        Write(root, "private/zz/stray", "not in the backup");
        console = new StringConsole();
        command = new DbCommand(console, new SystemFileSystem(), new FakeProcessRunner(), project);

        var restored = await command.ExecuteAsync(["restore", backup, "--yes"], CancellationToken.None);

        Assert.True(restored == 0, console.OutText + console.ErrorText);
        Assert.Equal("original bytes", File.ReadAllText(Path.Combine(root, "private/ab/avatar")));
        Assert.False(File.Exists(Path.Combine(root, "private/zz/stray")));
        Assert.DoesNotContain("no bytes", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restoring_a_database_without_its_archive_names_the_files_that_will_404()
    {
        var project = CreateProject();
        var backup = CreateDatabase("StoredFile", Path.Combine(_directory, "old.db"), ("private/ab/avatar", "Disk"));
        CreateDatabase("StoredFile", Path.Combine(project, "app.db"));
        var console = new StringConsole();
        var command = new DbCommand(console, new SystemFileSystem(), new FakeProcessRunner(), project);

        var restored = await command.ExecuteAsync(["restore", backup, "--yes"], CancellationToken.None);

        Assert.True(restored == 0, console.OutText + console.ErrorText);
        Assert.Contains("1 of 1 stored file(s)", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("private/ab/avatar", console.ErrorText, StringComparison.Ordinal);
    }

    private string CreateProject()
    {
        var project = Directory.CreateDirectory(Path.Combine(_directory, "app")).FullName;
        File.WriteAllText(
            Path.Combine(project, "App.csproj"),
            "<Project><ItemGroup><PackageReference Include=\"Microsoft.EntityFrameworkCore.Design\" /></ItemGroup></Project>");
        return project;
    }

    private string CreateDatabase(string table, params (string Key, string Provider)[] rows) =>
        CreateDatabase(table, Path.Combine(_directory, $"{Guid.NewGuid():N}.db"), rows);

    private static string CreateDatabase(string table, string path, params (string Key, string Provider)[] rows)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        Exec(connection, $"CREATE TABLE \"{table}\"(Id TEXT PRIMARY KEY, \"Key\" TEXT NOT NULL, Provider TEXT NOT NULL, Sha256 TEXT NOT NULL);");
        foreach (var (key, provider) in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO \"{table}\" VALUES ($id, $key, $provider, '')";
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("$key", key);
            insert.Parameters.AddWithValue("$provider", provider);
            insert.ExecuteNonQuery();
        }

        return path;
    }

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
