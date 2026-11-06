using System.Formats.Tar;
using System.IO.Compression;

namespace KitBundle;

/// <summary>Installs a lock: every tarball fetched, checked against its locked SHA-512, and unpacked where it is locked.</summary>
/// <remarks>
///     A tarball that passed its check is kept under the work directory by its digest, so a second build fetches
///     nothing — and a cached file is checked again on every read, since it is about to be bundled into the kit.
///     Nothing a package declares runs: no install scripts, no bin links. An engine needs its files, not its hooks.
/// </remarks>
internal static class Installer
{
    private static string Tarballs => Path.Combine(Repository.Work, "tarballs");

    /// <summary>Unpacks every package of <paramref name="packages" /> that installs here into a fresh <paramref name="root" />.</summary>
    internal static async Task Install(IEnumerable<LockedPackage> packages, string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        await Parallel.ForEachAsync(
            packages.Where(p => p.InstallsHere && !p.Path.StartsWith(BundleLock.DataPrefix, StringComparison.Ordinal)),
            async (package, _) => Unpack(await Fetch(package), Path.Combine(root, package.Path)));
    }

    /// <summary>The package's tarball, verified.</summary>
    internal static async Task<byte[]> Fetch(LockedPackage package)
    {
        var cached = Path.Combine(Tarballs, Convert.ToHexStringLower(Convert.FromBase64String(package.Integrity["sha512-".Length..]))[..32] + ".tgz");
        if (File.Exists(cached))
        {
            var bytes = await File.ReadAllBytesAsync(cached);
            if (string.Equals(NpmRegistry.Integrity(bytes), package.Integrity, StringComparison.Ordinal))
            {
                return bytes;
            }
        }

        var fetched = await NpmRegistry.Tarball(package.Resolved, package.Integrity);
        Directory.CreateDirectory(Tarballs);
        var staging = cached + "." + Guid.NewGuid().ToString("n")[..8];
        await File.WriteAllBytesAsync(staging, fetched);
        File.Move(staging, cached, overwrite: true);
        return fetched;
    }

    /// <summary>The regular files of an npm tarball, each relative to the package (its one root directory dropped).</summary>
    internal static IEnumerable<(string Path, byte[] Bytes)> Files(byte[] tarball)
    {
        using var gzip = new GZipStream(new MemoryStream(tarball), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
            {
                continue;
            }

            var slash = entry.Name.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0)
            {
                continue;
            }

            using var data = new MemoryStream();
            entry.DataStream.CopyTo(data);
            yield return (entry.Name[(slash + 1)..], data.ToArray());
        }
    }

    private static void Unpack(byte[] tarball, string directory)
    {
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        foreach (var (path, bytes) in Files(tarball))
        {
            var target = Path.GetFullPath(Path.Combine(root, path));
            if (!target.StartsWith(root, StringComparison.Ordinal))
            {
                throw new KitBundleException($"{directory}: the tarball names {path}, outside the package");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
    }
}
