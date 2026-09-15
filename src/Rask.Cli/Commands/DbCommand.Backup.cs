using Microsoft.Data.Sqlite;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask db backup</c> / <c>rask db restore</c> — get a copy of the database down, and a known-good copy
/// back up.
/// </summary>
/// <remarks>
/// <para>
///     <b>Why not just copy the file.</b> With WAL on — which every Rask app has, it is one of the
///     production pragmas — the <c>.db</c> file on its own is torn or stale: committed transactions live in
///     the <c>-wal</c> sidecar until a checkpoint. A backup has to go through SQLite, which is what the
///     Online Backup API and <c>VACUUM INTO</c> are for. Both produce a single, self-contained, already
///     checkpointed file, taken safely while the app keeps writing.
/// </para>
/// <para>
///     <b>Local</b> uses <c>Microsoft.Data.Sqlite</c>'s <c>BackupDatabase</c> — the Online Backup API, the
///     same call <c>Rask.SQLite.Snapshots</c> makes.
/// </para>
/// <para>
///     <b>Uploaded files go with it.</b> An app on Rask.Storage's disk provider keeps its uploads as files, and a
///     <c>StoredFile</c> row per upload in the database. A database restored without its files points at bytes that are
///     gone, so the files are archived beside the copy (<c>shop-….files.tgz</c> next to <c>shop-….db</c>) and restored
///     with it. The archive is taken after the database copy: a save writes its bytes before its row, so every row in
///     the copy has its bytes in the archive, and bytes saved in between are orphans the storage sweep removes.
/// </para>
/// <para>
///     <b>Remote</b> needs nothing installed on the host and nothing installed locally: it runs
///     <c>VACUUM INTO</c> inside a throwaway container mounted on the app's data volume, exactly the shape
///     the deploy's readiness probe already uses, and brings the result down with <c>docker cp</c>.
/// </para>
/// </remarks>
internal sealed partial class DbCommand
{
    /// <summary>
    ///     A pinned image with a <c>sqlite3</c> binary, used only as a tool: it is mounted on the data
    ///     volume, runs one command, and is removed. Alpine, because it is the smallest thing that is
    ///     unambiguously canonical — the alternative was requiring a <c>sqlite3</c> on the host, which is a
    ///     dependency we could neither pin nor install.
    /// </summary>
    private const string SqliteImage = "alpine:3.21";

    /// <summary>Where the container writes its copy, inside the mounted data volume.</summary>
    private const string RemoteBackupPath = "/data/.rask-backup.db";

    /// <summary>The database inside a deployed container — fixed by what <c>rask deploy</c> configures.</summary>
    private const string RemoteDatabasePath = "/data/app.db";

    /// <summary>Where the container writes the files archive, beside <see cref="RemoteBackupPath"/>.</summary>
    private const string RemoteFilesArchivePath = "/data/.rask-backup-files.tgz";

    /// <summary>Where the restore unpacks the archive before swapping it in.</summary>
    private const string RemoteFilesStagingPath = "/data/.rask-restore-files";

    /// <summary>
    /// Rask.Storage's disk root on a deployed container: <c>files</c> on the data volume. An app that sets
    /// <c>Rask__Storage__Disk__Root</c> elsewhere keeps its files outside what a backup can see.
    /// </summary>
    private const string RemoteFilesPath = "/data/files";

    /// <summary>
    /// <c>VACUUM INTO</c> the live database, then archive the stored files, inside a throwaway container on the app's
    /// data volume.
    /// </summary>
    /// <remarks>
    /// <c>VACUUM INTO</c> rather than a file copy because it is transactionally consistent against a
    /// database being written to, and it checkpoints the WAL into the output — so the single file that
    /// comes back is the whole database, not a torn one. <c>rm -f</c> first so a previous run that died
    /// between the vacuum and the cleanup cannot make this one fail with "output file already exists".
    /// <c>mkdir -p</c> before the archive so an app that has never stored a file still gets one, and the copy down
    /// never has to guess whether a missing archive is an app with no files or a failure.
    /// </remarks>
    internal static IReadOnlyList<string> BuildRemoteVacuumArguments(string host, string slug) =>
    [
        "-H", $"ssh://{host}", "run", "--rm", "-v", $"{slug}-data:/data", SqliteImage,
        "sh", "-c",
        $"rm -f {RemoteBackupPath} {RemoteFilesArchivePath} && apk add --no-cache sqlite >/dev/null && " +
        $"sqlite3 {RemoteDatabasePath} \"VACUUM INTO '{RemoteBackupPath}'\" && " +
        $"mkdir -p {RemoteFilesPath} && " +
        $"tar -C /data -czf {RemoteFilesArchivePath} --exclude=files/{StoredFilesArchive.SpoolDirectory} files",
    ];

    /// <summary>
    /// A stopped helper container holding the data volume, so <c>docker cp</c> has something to copy
    /// to or from. <c>docker cp</c> needs a container, not a volume, and a created-but-never-started one
    /// is the cheapest thing that satisfies it.
    /// </summary>
    internal static IReadOnlyList<string> BuildHelperCreateArguments(string host, string slug, string helper) =>
        ["-H", $"ssh://{host}", "create", "--name", helper, "-v", $"{slug}-data:/data", SqliteImage, "true"];

    internal static IReadOnlyList<string> BuildHelperRemoveArguments(string host, string helper) =>
        ["-H", $"ssh://{host}", "rm", "-f", helper];

    internal static IReadOnlyList<string> BuildCopyDownArguments(string host, string helper, string localPath) =>
        ["-H", $"ssh://{host}", "cp", $"{helper}:{RemoteBackupPath}", localPath];

    internal static IReadOnlyList<string> BuildCopyUpArguments(string host, string helper, string localPath) =>
        ["-H", $"ssh://{host}", "cp", localPath, $"{helper}:{RemoteBackupPath}"];

    internal static IReadOnlyList<string> BuildFilesCopyDownArguments(string host, string helper, string localPath) =>
        ["-H", $"ssh://{host}", "cp", $"{helper}:{RemoteFilesArchivePath}", localPath];

    internal static IReadOnlyList<string> BuildFilesCopyUpArguments(string host, string helper, string localPath) =>
        ["-H", $"ssh://{host}", "cp", localPath, $"{helper}:{RemoteFilesArchivePath}"];

    /// <summary>Delete the staged copies from inside the volume once they are down (or restored).</summary>
    internal static IReadOnlyList<string> BuildRemoteCleanupArguments(string host, string slug) =>
    [
        "-H", $"ssh://{host}", "run", "--rm", "-v", $"{slug}-data:/data", SqliteImage,
        "rm", "-rf", RemoteBackupPath, RemoteFilesArchivePath, RemoteFilesStagingPath,
    ];

    /// <summary>
    /// Move the uploaded copy over the live database, inside the volume.
    /// </summary>
    /// <remarks>
    /// The <c>-wal</c> and <c>-shm</c> sidecars are deleted in the same breath, and that is not tidiness:
    /// leaving a stale WAL beside a replaced database is how a restore silently produces a hybrid of the
    /// two, because SQLite will replay it over the file it now finds. The app must be stopped before this
    /// runs — <see cref="RestoreRemoteAsync" /> refuses otherwise.
    /// <para>
    /// With <paramref name="withFiles"/>, the uploaded archive is unpacked into a staging directory FIRST and swapped
    /// over the files directory only once it has come out whole, so a corrupt archive stops the restore before the
    /// database has been touched.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> BuildRemoteReplaceArguments(string host, string slug, bool withFiles = false) =>
    [
        "-H", $"ssh://{host}", "run", "--rm", "-v", $"{slug}-data:/data", SqliteImage,
        "sh", "-c",
        (withFiles
            ? $"rm -rf {RemoteFilesStagingPath} && mkdir {RemoteFilesStagingPath} && " +
              $"tar -C {RemoteFilesStagingPath} -xzf {RemoteFilesArchivePath} && "
            : "") +
        $"rm -f {RemoteDatabasePath}-wal {RemoteDatabasePath}-shm && " +
        $"mv {RemoteBackupPath} {RemoteDatabasePath}" +
        (withFiles
            ? $" && rm -rf {RemoteFilesPath} && mv {RemoteFilesStagingPath}/files {RemoteFilesPath} && " +
              $"rm -rf {RemoteFilesStagingPath} {RemoteFilesArchivePath}"
            : ""),
    ];

    internal static IReadOnlyList<string> BuildStopArguments(string host, string container) =>
        ["-H", $"ssh://{host}", "stop", container];

    internal static IReadOnlyList<string> BuildStartArguments(string host, string container) =>
        ["-H", $"ssh://{host}", "start", container];

    /// <summary>
    /// The default output name for a backup: the app's own name plus the instant, so successive backups
    /// never overwrite each other and sort chronologically.
    /// </summary>
    internal static string DefaultBackupName(string appName, DateTimeOffset now) =>
        $"{appName}-{now.UtcDateTime:yyyyMMdd-HHmmss}.db";

    /// <summary>
    /// Dispatch <c>backup</c>/<c>restore</c> to the local or remote path, resolving the deployment's host
    /// and app name from <c>.rask/deploy.json</c> when they aren't given.
    /// </summary>
    private async Task<int> ExecuteFileActionAsync(
        string subcommand,
        string? file,
        string projectDirectory,
        string? output,
        bool remote,
        string? host,
        string? app,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!remote)
        {
            return subcommand == "backup"
                ? await BackupLocalAsync(projectDirectory, output, cancellationToken).ConfigureAwait(false)
                : await RestoreLocalAsync(projectDirectory, file!, force, cancellationToken).ConfigureAwait(false);
        }

        var config = DeployConfig.Load(_fileSystem, _workingDirectory, Console);
        host ??= config.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            Console.WriteErrorLine(
                "No deployment host. Pass --host, or run this in a directory that has deployed before "
                + "(the host is remembered in .rask/deploy.json).",
                ConsoleStyle.Error);
            return 1;
        }

        // Every host string goes through the same parser `rask deploy` uses, so a value beginning with '-'
        // can never reach ssh as an option — a host of `-oProxyCommand=…` would otherwise run that command
        // on *this* machine, and the host comes from a file that is committed to the repository.
        if (!SshTarget.TryParse(host, out _, out var hostError))
        {
            Console.WriteErrorLine(hostError!, ConsoleStyle.Error);
            return 1;
        }

        var name = app ?? config.Name ?? Path.GetFileName(projectDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var slug = DeployCommand.ToContainerSlug(name);

        return subcommand == "backup"
            ? await BackupRemoteAsync(host, slug, output, cancellationToken).ConfigureAwait(false)
            : await RestoreRemoteAsync(host, slug, file!, force, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Copy the local database through SQLite's Online Backup API.</summary>
    private async Task<int> BackupLocalAsync(string projectDirectory, string? output, CancellationToken cancellationToken)
    {
        var (source, error) = SqliteDatabaseLocator.Locate(_fileSystem, projectDirectory);
        if (source is null)
        {
            Console.WriteErrorLine(error!, ConsoleStyle.Error);
            return 1;
        }

        if (!_fileSystem.FileExists(source))
        {
            Console.WriteErrorLine($"No database at '{source}'. Run 'rask db update' to create it first.", ConsoleStyle.Error);
            return 1;
        }

        var destination = ResolveOutputPath(output, Path.GetFileName(projectDirectory.TrimEnd(Path.DirectorySeparatorChar)));

        try
        {
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            await Task.Run(() => CopyDatabase(source, destination), cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            Console.WriteErrorLine($"Couldn't back up '{source}': {ex.Message}", ConsoleStyle.Error);
            return 1;
        }
        catch (IOException ex)
        {
            Console.WriteErrorLine($"Couldn't write '{destination}': {ex.Message}", ConsoleStyle.Error);
            return 1;
        }

        Console.WriteLine($"Backed up to {destination}.", ConsoleStyle.Success);

        // After the database, never before: see the type's remarks on why that order keeps rows and bytes together.
        var root = StorageRootLocator.Locate(_fileSystem, projectDirectory);
        if (root is null || !_fileSystem.DirectoryExists(root))
        {
            return 0;
        }

        var archive = StoredFilesArchive.PathFor(destination);
        try
        {
            var count = await Task.Run(() => StoredFilesArchive.Create(root, archive), cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Backed up {count} stored file(s) from {root} to {archive}.", ConsoleStyle.Success);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteErrorLine(
                $"The database is backed up, but the stored files under '{root}' are not: {ex.Message}",
                ConsoleStyle.Error);
            return 1;
        }
    }

    /// <summary>Replace the local database with <paramref name="input"/>, after confirming.</summary>
    private async Task<int> RestoreLocalAsync(string projectDirectory, string input, bool force, CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(input))
        {
            Console.WriteErrorLine(
                $"No such backup: '{input}'. 'rask db backup' writes into the current directory unless you "
                + "pass --output, and prints the path it used.",
                ConsoleStyle.Error);
            return 1;
        }

        var (destination, error) = SqliteDatabaseLocator.Locate(_fileSystem, projectDirectory);
        if (destination is null)
        {
            Console.WriteErrorLine(error!, ConsoleStyle.Error);
            return 1;
        }

        var root = StorageRootLocator.Locate(_fileSystem, projectDirectory);
        var archive = StoredFilesArchive.PathFor(input);
        var withFiles = root is not null && _fileSystem.FileExists(archive);

        var question = withFiles
            ? $"Restore '{input}' over the database at '{destination}', and '{archive}' over the stored files in '{root}'? This replaces both and everything in them."
            : $"Restore '{input}' over the database at '{destination}'? This replaces it and everything in it.";
        if (!TryConfirm(question, force, out var declined))
        {
            return declined;
        }

        if (withFiles)
        {
            // Files first: the swap only happens once the whole archive has come out, so a corrupt archive stops the
            // restore with the database untouched.
            try
            {
                var count = await Task.Run(() => StoredFilesArchive.Restore(archive, root!), cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"Restored {count} stored file(s) to {root}.", ConsoleStyle.Dim);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Console.WriteErrorLine(
                    $"Couldn't restore the stored files from '{archive}', so nothing was restored: {ex.Message}",
                    ConsoleStyle.Error);
                return 1;
            }
        }

        try
        {
            // Restore through SQLite too, so a truncated or non-SQLite file is rejected here rather than
            // discovered by the app at its next read — and so the WAL sidecars can never outlive the file
            // they belonged to.
            await Task.Run(
                () =>
                {
                    _fileSystem.TryDelete(destination + "-wal");
                    _fileSystem.TryDelete(destination + "-shm");
                    CopyDatabase(input, destination);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            Console.WriteErrorLine($"'{input}' isn't a readable SQLite database: {ex.Message}", ConsoleStyle.Error);
            return 1;
        }
        catch (IOException ex)
        {
            Console.WriteErrorLine($"Couldn't write '{destination}': {ex.Message}", ConsoleStyle.Error);
            return 1;
        }

        Console.WriteLine($"Restored {input} to {destination}.", ConsoleStyle.Success);

        if (root is not null)
        {
            ReportMissingStoredFiles(destination, root, withFiles);
        }

        return 0;
    }

    /// <summary>
    /// Say so when the restored database has disk rows whose bytes are not there — the 404s a restore without its
    /// files would otherwise leave to be discovered by users.
    /// </summary>
    private void ReportMissingStoredFiles(string databasePath, string root, bool restoredFiles)
    {
        MissingStoredFiles? found;
        try
        {
            found = StoredFilesCheck.FindMissing(databasePath, root);
        }
        catch (SqliteException)
        {
            return;
        }

        if (found is not { Missing: > 0 })
        {
            return;
        }

        var hint = restoredFiles
            ? "The archive did not hold them either, so they were already missing when the backup was taken."
            : "No .files.tgz archive was restored with the database; restore the one taken beside it to bring them back.";
        Console.WriteErrorLine(
            $"{found.Missing} of {found.Checked} stored file(s) in the restored database have no bytes under '{root}' "
            + $"(for example {string.Join(", ", found.Examples)}), and will answer 404. {hint}",
            ConsoleStyle.Warning);
    }

    // The Online Backup API: consistent against a live writer, and it checkpoints the WAL into the copy,
    // so the result is one self-contained file. Same call Rask.SQLite.Snapshots makes.
    private static void CopyDatabase(string source, string destination)
    {
        using var from = new SqliteConnection($"Data Source={source};Mode=ReadOnly;Pooling=False");
        using var to = new SqliteConnection($"Data Source={destination};Pooling=False");
        from.Open();
        to.Open();
        from.BackupDatabase(to);
    }

    private async Task<int> BackupRemoteAsync(string host, string slug, string? output, CancellationToken cancellationToken)
    {
        var destination = ResolveOutputPath(output, slug);
        var helper = $"rask-backup-{Guid.NewGuid():N}"[..24];

        Console.WriteLine($"Taking a consistent copy of {slug}'s database on {host}…", ConsoleStyle.Dim);
        if (await DockerAsync(BuildRemoteVacuumArguments(host, slug), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine("Couldn't copy the database and stored files inside the container. Is the app deployed, and does the host have network access to pull the sqlite image?", ConsoleStyle.Error);
            return 1;
        }

        try
        {
            if (await DockerAsync(BuildHelperCreateArguments(host, slug, helper), cancellationToken).ConfigureAwait(false) != 0)
            {
                Console.WriteErrorLine("Couldn't create the helper container that carries the copy down.", ConsoleStyle.Error);
                return 1;
            }

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            if (await DockerAsync(BuildCopyDownArguments(host, helper, destination), cancellationToken).ConfigureAwait(false) != 0)
            {
                Console.WriteErrorLine("Couldn't copy the backup down from the host.", ConsoleStyle.Error);
                return 1;
            }

            if (await DockerAsync(BuildFilesCopyDownArguments(host, helper, StoredFilesArchive.PathFor(destination)), cancellationToken).ConfigureAwait(false) != 0)
            {
                Console.WriteErrorLine(
                    $"The database is backed up to {destination}, but the stored files archive couldn't be copied down from the host.",
                    ConsoleStyle.Error);
                return 1;
            }
        }
        finally
        {
            // Best effort: a helper container or a staged file left behind is untidy, not harmful, and
            // must never turn a successful backup into a failure.
            await DockerAsync(BuildHelperRemoveArguments(host, helper), CancellationToken.None).ConfigureAwait(false);
            await DockerAsync(BuildRemoteCleanupArguments(host, slug), CancellationToken.None).ConfigureAwait(false);
        }

        Console.WriteLine(
            $"Backed up to {destination}, with the stored files in {RemoteFilesPath} beside it as {StoredFilesArchive.PathFor(destination)}.",
            ConsoleStyle.Success);
        return 0;
    }

    private async Task<int> RestoreRemoteAsync(string host, string slug, string input, bool force, CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(input))
        {
            Console.WriteErrorLine(
                $"No such backup: '{input}'. 'rask db backup' writes into the current directory unless you "
                + "pass --output, and prints the path it used.",
                ConsoleStyle.Error);
            return 1;
        }

        var archive = StoredFilesArchive.PathFor(input);
        var withFiles = _fileSystem.FileExists(archive);
        var question = withFiles
            ? $"Restore '{input}' and '{archive}' over {slug}'s database and stored files on {host}? The app stops, both are replaced, and it starts again."
            : $"Restore '{input}' over {slug}'s database on {host}? The app stops, its database is replaced, and it starts again. There is no '{archive}' beside it, so the stored files are left as they are.";
        if (!TryConfirm(question, force, out var declined))
        {
            return declined;
        }

        // Stopping first is not politeness. Replacing the file under a live writer leaves the running
        // process holding a handle to the database it thinks it has, and the next checkpoint writes that
        // belief back over the restored one — a corrupted hybrid of the two.
        Console.WriteLine($"Stopping {slug}…", ConsoleStyle.Dim);
        if (await DockerAsync(BuildStopArguments(host, slug), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine($"Couldn't stop '{slug}' on {host}. Refusing to restore under a running app — it would corrupt the database.", ConsoleStyle.Error);
            return 1;
        }

        var helper = $"rask-restore-{Guid.NewGuid():N}"[..24];
        var restored = false;
        try
        {
            if (await DockerAsync(BuildHelperCreateArguments(host, slug, helper), cancellationToken).ConfigureAwait(false) != 0)
            {
                Console.WriteErrorLine("Couldn't create the helper container that carries the copy up.", ConsoleStyle.Error);
                return 1;
            }

            if (await DockerAsync(BuildCopyUpArguments(host, helper, input), cancellationToken).ConfigureAwait(false) != 0)
            {
                Console.WriteErrorLine("Couldn't copy the backup up to the host.", ConsoleStyle.Error);
                return 1;
            }

            if (withFiles &&
                await DockerAsync(BuildFilesCopyUpArguments(host, helper, archive), cancellationToken).ConfigureAwait(false) != 0)
            {
                Console.WriteErrorLine("Couldn't copy the stored files archive up to the host.", ConsoleStyle.Error);
                return 1;
            }

            if (await DockerAsync(BuildRemoteReplaceArguments(host, slug, withFiles), cancellationToken).ConfigureAwait(false) != 0)
            {
                Console.WriteErrorLine("Couldn't put the database in place inside the volume.", ConsoleStyle.Error);
                return 1;
            }

            restored = true;
        }
        finally
        {
            await DockerAsync(BuildHelperRemoveArguments(host, helper), CancellationToken.None).ConfigureAwait(false);
            await DockerAsync(BuildRemoteCleanupArguments(host, slug), CancellationToken.None).ConfigureAwait(false);

            // Always bring the app back, restored or not: leaving it stopped after a failed restore turns
            // a recoverable problem into an outage.
            Console.WriteLine($"Starting {slug}…", ConsoleStyle.Dim);
            if (await DockerAsync(BuildStartArguments(host, slug), CancellationToken.None).ConfigureAwait(false) != 0)
            {
                Console.WriteErrorLine($"Couldn't start '{slug}' again — start it by hand: docker -H ssh://{host} start {slug}", ConsoleStyle.Error);
            }
        }

        if (!restored)
        {
            return 1;
        }

        Console.WriteLine(
            withFiles ? $"Restored {input} and {archive} to {slug} on {host}." : $"Restored {input} to {slug} on {host}.",
            ConsoleStyle.Success);
        return 0;
    }

    private Task<int> DockerAsync(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        _process.RunAsync("docker", args, _workingDirectory, cancellationToken);

    private string ResolveOutputPath(string? output, string appName)
    {
        var name = DefaultBackupName(appName, TimeProvider.System.GetUtcNow());
        if (string.IsNullOrWhiteSpace(output))
        {
            return Path.GetFullPath(Path.Combine(_workingDirectory, name));
        }

        // A directory (existing, or written with a trailing separator) means "put it in here"; anything
        // else is the file name the user chose.
        var looksLikeDirectory = output.EndsWith(Path.DirectorySeparatorChar) ||
                                 output.EndsWith('/') ||
                                 Directory.Exists(output);

        var full = Path.GetFullPath(Path.Combine(_workingDirectory, output));
        return looksLikeDirectory ? Path.Combine(full, name) : full;
    }

    /// <summary>
    /// The contract <c>rask db drop</c> set: refuse rather than guess when there is no terminal to ask on,
    /// and treat "no" as a completed command rather than an error.
    /// </summary>
    /// <returns>
    /// <c>true</c> to go ahead. Otherwise <paramref name="exitCode"/> distinguishes the two ways of not
    /// going ahead — <c>0</c> for a deliberate "no", <c>1</c> for nobody to ask.
    /// </returns>
    private bool TryConfirm(string question, bool force, out int exitCode)
    {
        exitCode = 0;
        if (force)
        {
            return true;
        }

        if (Console.IsInputRedirected)
        {
            Console.WriteErrorLine(
                "This replaces a database. Pass --yes to confirm — there's no terminal to ask on.",
                ConsoleStyle.Error);
            exitCode = 1;
            return false;
        }

        if (new Prompt(Console).Confirm(question, @default: false))
        {
            return true;
        }

        Console.Out.WriteLine("Left it alone.");
        return false;
    }
}
