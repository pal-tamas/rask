using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// Finds the directory Rask.Storage's disk provider keeps an app's uploads in, by reading the settings the app reads.
/// </summary>
/// <remarks>
/// Mirrors <c>StorageConfiguration.ResolveDiskRoot</c>: <c>Rask:Storage:Disk:Root</c> when set (relative to the
/// content root), else <c>files</c> on the deploy volume when <c>/data</c> exists, else <c>storage/</c> under the
/// content root. Returns null when the app is configured for S3 or Azure — those have no files on this machine.
/// </remarks>
internal static class StorageRootLocator
{
    /// <summary>The deploy volume a Rask container mounts; the storage default when it exists.</summary>
    internal const string DataVolume = "/data";

    internal static string? Locate(IFileSystem fileSystem, string projectDirectory, string? environment = null)
    {
        var provider = AppSettingsReader.ReadStrings(fileSystem, projectDirectory, environment, "Rask", "Storage", "Provider")
            .FirstOrDefault();
        if (provider is not null && !provider.Equals("Disk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (AppSettingsReader.ReadStrings(fileSystem, projectDirectory, environment, "Rask", "Storage", "Disk", "Root")
                .FirstOrDefault(root => root.Length > 0) is { } configured)
        {
            return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(projectDirectory, configured));
        }

        return fileSystem.DirectoryExists(DataVolume)
            ? Path.GetFullPath(Path.Combine(DataVolume, "files"))
            : Path.GetFullPath(Path.Combine(projectDirectory, "storage"));
    }
}

/// <summary>
/// The stored files of a disk-backed app as one <c>.tar.gz</c>, kept beside the database backup it belongs to.
/// </summary>
internal static class StoredFilesArchive
{
    /// <summary>Rask.Storage's spool for saves in progress — never part of a backup.</summary>
    internal const string SpoolDirectory = ".tmp";

    /// <summary>The archive that belongs with <paramref name="databaseBackupPath"/>: <c>shop-….db</c> → <c>shop-….files.tgz</c>.</summary>
    internal static string PathFor(string databaseBackupPath) =>
        Path.ChangeExtension(databaseBackupPath, null) + ".files.tgz";

    /// <summary>Archive every file under <paramref name="root"/> except the spool. Returns how many went in.</summary>
    /// <remarks>
    /// Written to a partial file and moved into place, so a backup that dies half-way never leaves a truncated
    /// archive beside a good database for a later restore to trust.
    /// </remarks>
    internal static int Create(string root, string archivePath)
    {
        var partial = archivePath + ".partial";
        var count = 0;
        try
        {
            using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                    if (relative.StartsWith(SpoolDirectory + "/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    tar.WriteEntry(path, relative);
                    count++;
                }
            }

            File.Move(partial, archivePath, overwrite: true);
            return count;
        }
        finally
        {
            File.Delete(partial);
        }
    }

    /// <summary>
    /// Replace everything under <paramref name="root"/> with the archive's contents. Returns how many files came back.
    /// </summary>
    /// <remarks>
    /// Extracted into a staging directory beside the root first, and swapped in only once the whole archive has come
    /// out: a corrupt archive leaves the current files where they are. <see cref="TarFile.ExtractToDirectory(Stream, string, bool)"/>
    /// refuses entries and links that resolve outside the destination.
    /// </remarks>
    internal static int Restore(string archivePath, string root)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var staging = $"{trimmed}.rask-restore-{Guid.NewGuid():N}";
        var previous = $"{trimmed}.rask-previous-{Guid.NewGuid():N}";

        Directory.CreateDirectory(staging);
        try
        {
            using (var file = File.OpenRead(archivePath))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                TarFile.ExtractToDirectory(gzip, staging, overwriteFiles: false);
            }

            var count = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Count();

            if (Directory.Exists(trimmed))
            {
                Directory.Move(trimmed, previous);
            }

            Directory.Move(staging, trimmed);
            if (Directory.Exists(previous))
            {
                Directory.Delete(previous, recursive: true);
            }

            return count;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}

/// <summary>What <see cref="StoredFilesCheck.FindMissing"/> found.</summary>
/// <param name="Checked">Disk-provider rows looked at.</param>
/// <param name="Missing">Rows whose bytes are not under the root.</param>
/// <param name="Examples">The first few missing keys, for the message.</param>
internal sealed record MissingStoredFiles(int Checked, int Missing, IReadOnlyList<string> Examples);

/// <summary>
/// Rows without bytes: <c>StoredFile</c> rows on the disk provider whose file is not under the storage root — what a
/// database restored without its files looks like, and what would otherwise surface only as 404s.
/// </summary>
internal static class StoredFilesCheck
{
    private const int ExampleCount = 5;

    /// <summary>
    /// Check every disk row in <paramref name="databasePath"/> against <paramref name="root"/>. Null when the database
    /// has no stored-file table.
    /// </summary>
    /// <remarks>
    /// The table is found by its columns rather than by name: EF names it <c>StoredFile</c> by convention, but an app
    /// that exposes a <c>DbSet&lt;StoredFile&gt;</c> gets the property's name instead.
    /// </remarks>
    internal static MissingStoredFiles? FindMissing(string databasePath, string root)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();

        var table = FindTable(connection);
        if (table is null)
        {
            return null;
        }

        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var examples = new List<string>();
        int checkedRows = 0, missing = 0;

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"Key\" FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\" WHERE \"Provider\" = 'Disk'";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            checkedRows++;
            var key = reader.GetString(0);
            var path = Path.GetFullPath(Path.Combine(boundary, key));
            if (path.StartsWith(boundary, StringComparison.Ordinal) && File.Exists(path))
            {
                continue;
            }

            missing++;
            if (examples.Count < ExampleCount)
            {
                examples.Add(key);
            }
        }

        return new MissingStoredFiles(checkedRows, missing, examples);
    }

    private static string? FindTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT m.name FROM sqlite_master m
            WHERE m.type = 'table'
              AND EXISTS (SELECT 1 FROM pragma_table_info(m.name) WHERE name = 'Key')
              AND EXISTS (SELECT 1 FROM pragma_table_info(m.name) WHERE name = 'Provider')
              AND EXISTS (SELECT 1 FROM pragma_table_info(m.name) WHERE name = 'Sha256')
            ORDER BY m.name = 'StoredFile' DESC
            LIMIT 1
            """;
        return command.ExecuteScalar() as string;
    }
}
