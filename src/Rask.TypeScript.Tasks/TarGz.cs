using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Rask.TypeScript.Tasks;

/// <summary>
///     Unpacks a gzipped tar archive — enough of one to unpack an npm package, and no more.
/// </summary>
/// <remarks>
///     <para>
///         This exists because the download is a <c>.tgz</c> rather than the bare binary the Tailwind
///         resolver fetches, and <c>System.Formats.Tar</c> arrived in .NET 7 while this assembly targets
///         netstandard2.0 to load inside MSBuild. <c>GZipStream</c> is available there; a tar reader is
///         not.
///     </para>
///     <para>
///         Shelling out to <c>tar</c> would be shorter and is deliberately not done. It is absent from
///         older Windows, and the bsdtar that ships with Windows 10+ disagrees with GNU tar about flags
///         and about how it reports failure — so the shortcut trades eighty lines here for a class of
///         defect that only appears on other people's machines.
///     </para>
///     <para>
///         Tar is a sequence of 512-byte headers, each followed by its file's bytes padded to the next
///         512-byte boundary. Only what npm packages actually contain is handled: regular files,
///         directories, GNU long names, and pax extended headers (skipped — their payload restates a
///         path the ustar header also carries, which is enough for these two packages).
///     </para>
/// </remarks>
internal static class TarGz
{
    private const int BlockSize = 512;

    /// <summary>
    ///     Extract every file into <paramref name="destinationDirectory" />, dropping the archive's
    ///     single root directory.
    /// </summary>
    /// <remarks>
    ///     npm wraps every package in a <c>package/</c> directory. Stripping it is what lets the cache
    ///     path hold <c>bin/esbuild</c> and <c>lib/tsgo</c> rather than a <c>package</c> level that means
    ///     nothing to anyone reading the cache.
    /// </remarks>
    /// <returns>The relative paths written, in archive order.</returns>
    public static IReadOnlyList<string> ExtractTo(byte[] archive, string destinationDirectory, bool stripRoot = true)
    {
        if (archive is null)
        {
            throw new ArgumentNullException(nameof(archive));
        }

        var written = new List<string>();
        var full = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(full);

        using var raw = new MemoryStream(archive, writable: false);
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);

        // Decompressed up front rather than streamed: GZipStream cannot seek, tar needs to skip over
        // entry payloads, and these archives are tens of megabytes — small enough that buffering is
        // simpler than a read-and-discard loop, and the alternative is a partial-read bug waiting to
        // happen at a block boundary.
        var tar = ReadAll(gzip);

        var offset = 0;
        string? pendingLongName = null;

        while (offset + BlockSize <= tar.Length)
        {
            var header = new ArraySegment<byte>(tar, offset, BlockSize);
            offset += BlockSize;

            // Two consecutive zero blocks end the archive; one is enough to stop on, because nothing
            // legitimate follows and trailing garbage is not ours to interpret.
            if (IsAllZero(header))
            {
                break;
            }

            var size = ParseOctal(tar, offset - BlockSize + 124, 12);
            var typeFlag = (char)tar[offset - BlockSize + 156];
            var name = pendingLongName ?? ReadName(tar, offset - BlockSize);
            pendingLongName = null;

            var payload = offset;
            offset += Padded(size);

            switch (typeFlag)
            {
                case 'L':
                    // GNU long name: the NEXT entry's path is this entry's payload.
                    pendingLongName = Encoding.UTF8
                        .GetString(tar, payload, (int)size)
                        .TrimEnd('\0');
                    continue;

                case 'x':
                case 'g':
                    // pax extended header. Its payload restates metadata the ustar header already
                    // carries for these packages, so skipping it is correct rather than lossy.
                    continue;

                case '5':
                    continue;

                case '0':
                case '\0':
                    break;

                default:
                    // Links, devices, and anything else an npm package has no business containing.
                    continue;
            }

            var relative = stripRoot ? StripRoot(name) : name;
            if (relative.Length == 0)
            {
                continue;
            }

            var target = SafeCombine(full, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            using (var file = File.Create(target))
            {
                file.Write(tar, payload, (int)size);
            }

            written.Add(relative);
        }

        return written;
    }

    /// <summary>
    ///     Combine and refuse anything that escapes the destination.
    /// </summary>
    /// <remarks>
    ///     An archive entry named <c>../../.bashrc</c> is the classic tar traversal, and it matters more
    ///     here than usual: this method's output is about to be marked executable and run by the build.
    ///     The checksum makes a tampered archive unlikely, and unlikely is not a reason to skip the
    ///     cheap check.
    /// </remarks>
    private static string SafeCombine(string root, string relative)
    {
        var combined = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new IOException($"the archive contains an entry that escapes the extraction directory ('{relative}')");
        }

        return combined;
    }

    /// <summary>Drop the archive's leading directory component — npm's <c>package/</c>.</summary>
    private static string StripRoot(string name)
    {
        var normalized = name.Replace('\\', '/').TrimStart('/');
        var slash = normalized.IndexOf('/');
        return slash < 0 ? string.Empty : normalized.Substring(slash + 1);
    }

    /// <summary>The ustar name, which is a 155-byte prefix and a 100-byte name that join with a slash.</summary>
    private static string ReadName(byte[] tar, int headerStart)
    {
        var name = ReadString(tar, headerStart, 100);

        // "ustar" in the magic field means the prefix field is meaningful. Reading it unconditionally
        // would splice bytes from an unrelated field on the older format.
        var magic = ReadString(tar, headerStart + 257, 5);
        if (!string.Equals(magic, "ustar", StringComparison.Ordinal))
        {
            return name;
        }

        var prefix = ReadString(tar, headerStart + 345, 155);
        return prefix.Length == 0 ? name : prefix + "/" + name;
    }

    private static string ReadString(byte[] tar, int start, int length)
    {
        var end = start;
        var limit = Math.Min(start + length, tar.Length);
        while (end < limit && tar[end] != 0)
        {
            end++;
        }

        return Encoding.UTF8.GetString(tar, start, end - start);
    }

    /// <summary>
    ///     Tar stores numbers as NUL/space-terminated octal text.
    /// </summary>
    private static long ParseOctal(byte[] tar, int start, int length)
    {
        long value = 0;
        for (var i = start; i < start + length && i < tar.Length; i++)
        {
            var c = tar[i];
            if (c is 0 or (byte)' ')
            {
                // Trailing padding. Anything after it is padding too.
                if (value > 0)
                {
                    break;
                }

                continue;
            }

            if (c is < (byte)'0' or > (byte)'7')
            {
                throw new IOException(
                    $"the archive has a malformed size field (byte 0x{c.ToString("x2", CultureInfo.InvariantCulture)})");
            }

            value = (value * 8) + (c - '0');
        }

        return value;
    }

    private static int Padded(long size) => (int)(((size + BlockSize - 1) / BlockSize) * BlockSize);

    private static bool IsAllZero(ArraySegment<byte> block)
    {
        for (var i = 0; i < block.Count; i++)
        {
            if (block.Array![block.Offset + i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
