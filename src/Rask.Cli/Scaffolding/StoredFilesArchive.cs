using System.Formats.Tar;
using System.IO.Compression;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// The stored files of a disk-backed app as one <c>.tar.gz</c>, kept beside the database backup it belongs to.
/// </summary>
/// <remarks>
/// <b>One layout, wherever it was taken.</b> Every entry sits under <c>files/</c> — the directory's name on a deployed
/// app's data volume, which is what the remote backup archives — so a backup taken on a laptop restores on the host
/// and the other way round. Archives an older CLI took locally hold the root's contents at the top instead; a restore
/// reads them too (<see cref="IsRooted"/>), and one bound for the host is re-rooted first (<see cref="Reroot"/>).
/// </remarks>
internal static class StoredFilesArchive
{
    /// <summary>Rask.Storage's spool for saves in progress — never part of a backup.</summary>
    internal const string SpoolDirectory = ".tmp";

    /// <summary>The one directory every entry sits under.</summary>
    internal const string Folder = "files";

    private const string Prefix = Folder + "/";

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
                // The folder itself too, so an app with no files yet still has one to restore.
                tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, Prefix));
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                    if (relative.StartsWith(SpoolDirectory + "/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    tar.WriteEntry(path, Prefix + relative);
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
            var rooted = IsRooted(archivePath);
            using (var file = File.OpenRead(archivePath))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                TarFile.ExtractToDirectory(gzip, staging, overwriteFiles: false);
            }

            var tree = rooted ? Path.Combine(staging, Folder) : staging;
            Directory.CreateDirectory(tree);
            var count = Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories).Count();

            if (Directory.Exists(trimmed))
            {
                Directory.Move(trimmed, previous);
            }

            Directory.Move(tree, trimmed);
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

    /// <summary>
    /// Whether every entry sits under <c>files/</c> — the layout <see cref="Create"/> and the remote backup write. An
    /// archive an older CLI took locally holds the root's contents at the top, and answers false.
    /// </summary>
    /// <remarks>
    /// The one archive this misreads: an older local one whose storage root held nothing but a top-level <c>files/</c>
    /// directory. Rask.Storage keys start <c>public/</c> or <c>private/</c>, so that takes a custom key prefix of
    /// <c>files/</c>; such an archive restores one level too deep. Re-take the backup with this CLI to be sure.
    /// </remarks>
    internal static bool IsRooted(string archivePath)
    {
        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var any = false;
        while (tar.GetNextEntry() is { } entry)
        {
            var name = Name(entry.Name);
            if (name.Length == 0)
            {
                continue;
            }

            if (!string.Equals(name, Folder, StringComparison.Ordinal) && !name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            any = true;
        }

        return any;
    }

    /// <summary>
    /// Write <paramref name="archivePath"/> to <paramref name="reRooted"/> with every entry moved under <c>files/</c>:
    /// what an archive an older CLI took locally needs before the host can restore it.
    /// </summary>
    /// <remarks>Each entry's bytes are copied out before the next is read: a gzip stream cannot be written from in place.</remarks>
    internal static void Reroot(string archivePath, string reRooted)
    {
        using var source = File.OpenRead(archivePath);
        using var unzip = new GZipStream(source, CompressionMode.Decompress);
        using var reader = new TarReader(unzip);
        using var target = new FileStream(reRooted, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new GZipStream(target, CompressionLevel.Optimal);
        using var writer = new TarWriter(zip, TarEntryFormat.Pax, leaveOpen: false);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, Prefix));
        while (reader.GetNextEntry(copyData: true) is { } entry)
        {
            var name = Name(entry.Name);
            if (name.Length == 0)
            {
                continue;
            }

            entry.Name = Prefix + name + (entry.EntryType is TarEntryType.Directory ? "/" : "");
            writer.WriteEntry(entry);
        }
    }

    // An entry's path without a leading "./" or a trailing "/", as tar writes them either way.
    private static string Name(string name)
    {
        var trimmed = name.StartsWith("./", StringComparison.Ordinal) ? name[2..] : name;
        return trimmed.TrimEnd('/');
    }
}
