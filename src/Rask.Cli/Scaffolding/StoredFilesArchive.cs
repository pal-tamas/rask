using System.Formats.Tar;
using System.IO.Compression;

namespace Rask.Cli.Scaffolding;

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
