using System.IO.Compression;
using System.Text;

namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     The tar reader, exercised against archives this test builds itself.
/// </summary>
/// <remarks>
///     Built in-process rather than committed as a fixture <c>.tgz</c>, so that what each case proves
///     is visible in the test rather than sealed inside a binary nobody will open. The one thing a real
///     npm tarball would add — that the format assumptions hold against the genuine article — is
///     covered by the resolver actually running in the type-check gate.
/// </remarks>
public class TarGzTests
{
    [Fact]
    public void ExtractTo_WritesFilesAndDropsTheLeadingPackageDirectory()
    {
        var archive = TarBuilder.Create(
            ("package/bin/esbuild", "#!binary"),
            ("package/package.json", "{}"));

        using var temp = new TempDirectory();
        var written = TarGz.ExtractTo(archive, temp.Path);

        Assert.Equal(new[] { "bin/esbuild", "package.json" }, written);
        Assert.Equal("#!binary", File.ReadAllText(Path.Combine(temp.Path, "bin", "esbuild")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(temp.Path, "package.json")));
    }

    /// <summary>
    ///     Nested directories are created even though the archive lists no directory entries for them.
    /// </summary>
    /// <remarks>
    ///     npm tarballs frequently carry file entries whose parent directories were never emitted as
    ///     their own entries. Relying on a <c>'5'</c> entry to have created the directory first works on
    ///     archives that happen to include them and throws <c>DirectoryNotFoundException</c> on the ones
    ///     that do not.
    /// </remarks>
    [Fact]
    public void ExtractTo_CreatesParentDirectoriesWithNoDirectoryEntries()
    {
        var archive = TarBuilder.Create(("package/lib/deep/nested/lib.dom.d.ts", "declare var x: number;"));

        using var temp = new TempDirectory();
        TarGz.ExtractTo(archive, temp.Path);

        Assert.True(File.Exists(Path.Combine(temp.Path, "lib", "deep", "nested", "lib.dom.d.ts")));
    }

    /// <summary>
    ///     tsgo's shape: one binary beside a hundred-odd type-definition files, all of which matter.
    /// </summary>
    [Fact]
    public void ExtractTo_KeepsTheWholeTreeNotJustTheBinary()
    {
        var archive = TarBuilder.Create(
            ("package/lib/tsgo", "#!binary"),
            ("package/lib/lib.dom.d.ts", "interface Window {}"),
            ("package/lib/lib.es5.d.ts", "interface Array<T> {}"),
            ("package/LICENSE", "Apache"));

        using var temp = new TempDirectory();
        var written = TarGz.ExtractTo(archive, temp.Path);

        Assert.Equal(4, written.Count);
        Assert.True(File.Exists(Path.Combine(temp.Path, "lib", "lib.dom.d.ts")));
    }

    /// <summary>
    ///     An entry that climbs out of the destination is refused rather than written.
    /// </summary>
    /// <remarks>
    ///     The classic tar traversal, and it matters more here than usual: what this method writes is
    ///     about to be marked executable and run by the build. The checksum makes a tampered archive
    ///     unlikely, and unlikely is not a reason to skip a cheap check.
    /// </remarks>
    [Fact]
    public void ExtractTo_RefusesAnEntryThatEscapesTheDestination()
    {
        var archive = TarBuilder.Create(("package/../../escaped.sh", "rm -rf /"));

        using var temp = new TempDirectory();
        var ex = Assert.Throws<IOException>(() => TarGz.ExtractTo(archive, temp.Path));

        Assert.Contains("escapes the extraction directory", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(temp.Path, "..", "..", "escaped.sh")));
    }

    /// <summary>Directory entries contribute no files of their own.</summary>
    [Fact]
    public void ExtractTo_SkipsDirectoryEntries()
    {
        var archive = TarBuilder.Create(
            ("package/lib/", null),
            ("package/lib/tsgo", "#!binary"));

        using var temp = new TempDirectory();
        var written = TarGz.ExtractTo(archive, temp.Path);

        Assert.Equal(new[] { "lib/tsgo" }, written);
    }

    /// <summary>
    ///     A file whose length is not a multiple of 512 still leaves the reader on a block boundary.
    /// </summary>
    /// <remarks>
    ///     The single most likely way a hand-written tar reader goes wrong: forget to skip the padding
    ///     and every entry after the first is read from the middle of the previous one's data. The
    ///     three sizes here straddle a block boundary in both directions.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(2048)]
    public void ExtractTo_HandlesPaddingForAnySize(int size)
    {
        var body = new string('x', size);
        var archive = TarBuilder.Create(("package/first.bin", body), ("package/second.txt", "second"));

        using var temp = new TempDirectory();
        TarGz.ExtractTo(archive, temp.Path);

        Assert.Equal(body, File.ReadAllText(Path.Combine(temp.Path, "first.bin")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(temp.Path, "second.txt")));
    }

    /// <summary>An empty file is a real entry, not the end-of-archive marker.</summary>
    [Fact]
    public void ExtractTo_WritesZeroLengthFiles()
    {
        var archive = TarBuilder.Create(("package/empty", string.Empty), ("package/after", "after"));

        using var temp = new TempDirectory();
        TarGz.ExtractTo(archive, temp.Path);

        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(temp.Path, "empty")));
        Assert.Equal("after", File.ReadAllText(Path.Combine(temp.Path, "after")));
    }

    /// <summary>The ustar prefix field joins with the name field, for paths over 100 bytes.</summary>
    [Fact]
    public void ExtractTo_JoinsTheUstarPrefixWithTheName()
    {
        var longDirectory = "package/" + string.Join("/", Enumerable.Repeat("directory", 12));
        var archive = TarBuilder.Create((longDirectory + "/lib.d.ts", "declare var y: string;"));

        using var temp = new TempDirectory();
        var written = TarGz.ExtractTo(archive, temp.Path);

        Assert.Single(written);
        Assert.EndsWith("lib.d.ts", written[0], StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(temp.Path, written[0].Replace('/', Path.DirectorySeparatorChar))));
    }

    /// <summary>Content after the two-zero-block terminator is not part of the archive.</summary>
    [Fact]
    public void ExtractTo_StopsAtTheEndOfArchiveMarker()
    {
        var archive = TarBuilder.Create(("package/only.txt", "only"));

        using var temp = new TempDirectory();
        var written = TarGz.ExtractTo(archive, temp.Path);

        Assert.Equal(new[] { "only.txt" }, written);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "rask-targz-" + Guid.NewGuid().ToString("n").Substring(0, 8));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }
    }

    /// <summary>Builds a gzipped ustar archive — the smallest thing that is a real tar.</summary>
    private static class TarBuilder
    {
        private const int BlockSize = 512;

        /// <summary>A null body means a directory entry.</summary>
        public static byte[] Create(params (string Name, string? Body)[] entries)
        {
            using var tar = new MemoryStream();
            foreach (var (name, body) in entries)
            {
                var bytes = body is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body);
                WriteHeader(tar, name, bytes.Length, isDirectory: body is null);
                tar.Write(bytes, 0, bytes.Length);
                Pad(tar, bytes.Length);
            }

            // Two zero blocks terminate the archive.
            tar.Write(new byte[BlockSize * 2], 0, BlockSize * 2);

            using var gz = new MemoryStream();
            using (var deflate = new GZipStream(gz, CompressionMode.Compress, leaveOpen: true))
            {
                var raw = tar.ToArray();
                deflate.Write(raw, 0, raw.Length);
            }

            return gz.ToArray();
        }

        private static void WriteHeader(Stream stream, string name, int size, bool isDirectory)
        {
            var header = new byte[BlockSize];

            // A name over 100 bytes splits across the ustar prefix (offset 345) and name (offset 0)
            // fields, at a slash. That is the case the reader has to rejoin.
            var prefix = string.Empty;
            if (name.Length > 100)
            {
                var split = name.LastIndexOf('/', Math.Min(name.Length - 1, 155));
                prefix = name.Substring(0, split);
                name = name.Substring(split + 1);
            }

            Write(header, 0, name, 100);
            Write(header, 100, "0000755", 8);
            Write(header, 108, "0000000", 8);
            Write(header, 116, "0000000", 8);
            Write(header, 124, Convert.ToString(size, 8).PadLeft(11, '0'), 12);
            Write(header, 136, "00000000000", 12);
            header[156] = (byte)(isDirectory ? '5' : '0');
            Write(header, 257, "ustar", 6);
            Write(header, 263, "00", 2);
            Write(header, 345, prefix, 155);

            // The checksum is computed with the checksum field itself read as spaces. Nothing in the
            // reader verifies it, but writing a wrong one would make these archives invalid for anything
            // else that ever looks at them.
            for (var i = 148; i < 156; i++)
            {
                header[i] = (byte)' ';
            }

            var sum = header.Aggregate(0, (running, b) => running + b);
            Write(header, 148, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0", 8);

            stream.Write(header, 0, BlockSize);
        }

        private static void Write(byte[] header, int offset, string value, int length)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Array.Copy(bytes, 0, header, offset, Math.Min(bytes.Length, length));
        }

        private static void Pad(Stream stream, int size)
        {
            var remainder = size % BlockSize;
            if (remainder != 0)
            {
                stream.Write(new byte[BlockSize - remainder], 0, BlockSize - remainder);
            }
        }
    }
}
