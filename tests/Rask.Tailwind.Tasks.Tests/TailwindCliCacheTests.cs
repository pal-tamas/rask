using System;
using System.IO;
using System.Runtime.InteropServices;
using Rask.Tailwind.Tasks;

namespace Rask.Tailwind.Tasks.Tests;

/// <summary>
///     Whether a binary already in the per-user cache may be reused.
/// </summary>
/// <remarks>
///     <para>
///         Presence is not the question. The cached CLI is restored on CI by untarring it, and an untar
///         that fails part way leaves a file that exists and cannot run: actions/cache reports the
///         failure as a warning and the build carries on. The two shapes that leaves behind are a file
///         whose executable bit is gone — which surfaces as exit code <c>126</c> inside MSB3073, reading
///         like the CLI rejected its arguments — and a truncated one.
///     </para>
///     <para>
///         Both used to be permanent: the broken file kept satisfying <c>File.Exists</c>, so the
///         checksummed download that would have replaced it was never reached, and every later build on
///         that machine failed the same way.
///     </para>
/// </remarks>
public class TailwindCliCacheTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "rask-tailwind-cache-" + Path.GetRandomFileName());

    public TailwindCliCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Fact]
    public void A_binary_that_was_never_cached_is_not_reused()
    {
        Assert.False(TailwindCli.ReuseCached(Path.Combine(_dir, "tailwindcss-linux-x64")));
    }

    [Fact]
    public void A_cached_binary_is_reused()
    {
        var path = Cached("tailwindcss-linux-x64", "#!/bin/sh\nexit 0\n");

        Assert.True(TailwindCli.ReuseCached(path));
    }

    [Fact]
    public void A_truncated_binary_is_dropped_rather_than_reused()
    {
        // The file a cancelled or failed restore leaves: present, zero bytes, and not an executable at
        // all. Reusing it is what made the failure permanent.
        var path = Cached("tailwindcss-linux-x64", string.Empty);

        Assert.False(TailwindCli.ReuseCached(path));

        // Deleted, so the download that follows can put a checksummed one in its place rather than
        // finding this one still sitting there.
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Dropping_a_truncated_binary_says_so()
    {
        var path = Cached("tailwindcss-linux-x64", string.Empty);
        var said = string.Empty;

        TailwindCli.ReuseCached(path, m => said = m);

        // Named, because the alternative is a silent re-download that looks like the cache simply missed.
        Assert.Contains("empty", said, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(path, said, StringComparison.Ordinal);
    }

    [Fact]
    public void Reusing_a_cached_binary_restores_the_executable_bit()
    {
        // The nightly failure itself: mode bits lost in the restore, so Exec's /bin/sh exits 126. Reuse
        // has to repair it, or the cache poisons every later build on the machine.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var path = Cached("tailwindcss-linux-x64", "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Assert.True(TailwindCli.ReuseCached(path));
        Assert.True(File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute));
    }

    private string Cached(string name, string contents)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, contents);
        return path;
    }
}
