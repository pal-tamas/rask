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
///         failure as a warning and the build carries on. MSBuild then reports MSB3073 with the whole
///         command line in it — which reads as though the CLI rejected its arguments — over an exit code
///         of <c>126</c> when the executable bit was lost, or <c>127</c> when the file is truncated.
///         Both were seen on nightly, in that order, the second only after the first was fixed.
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
    public void A_binary_cached_with_its_receipt_is_reused()
    {
        var path = Fetched("tailwindcss-linux-x64", "#!/bin/sh\nexit 0\n");

        Assert.True(TailwindCli.ReuseCached(path));
    }

    // The failure that outlived the first fix. A partial restore leaves a file that is SHORTER than the
    // one that was verified — not empty, so a size-zero check walks straight past it, and executable, so
    // it is run: exit 127, "not an executable" rather than "not executable".
    [Fact]
    public void A_truncated_binary_is_dropped_rather_than_reused()
    {
        var path = Fetched("tailwindcss-linux-x64", "#!/bin/sh\nexit 0\n");
        File.WriteAllText(path, "#!/bin/sh\n");

        Assert.False(TailwindCli.ReuseCached(path));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(TailwindCli.ReceiptPath(path)));
    }

    [Fact]
    public void An_empty_binary_is_dropped_rather_than_reused()
    {
        var path = Fetched("tailwindcss-linux-x64", "#!/bin/sh\nexit 0\n");
        File.WriteAllText(path, string.Empty);

        Assert.False(TailwindCli.ReuseCached(path));
    }

    // A cache seeded by hand, or restored without its receipt, is not something to execute on trust.
    [Fact]
    public void A_binary_with_no_receipt_is_not_reused()
    {
        var path = Path.Combine(_dir, "tailwindcss-linux-x64");
        File.WriteAllText(path, "#!/bin/sh\nexit 0\n");

        Assert.False(TailwindCli.ReuseCached(path));
    }

    [Fact]
    public void Dropping_an_untrustworthy_binary_says_why()
    {
        var path = Fetched("tailwindcss-linux-x64", "#!/bin/sh\nexit 0\n");
        File.WriteAllText(path, "#!/bin/sh\n");
        var said = string.Empty;

        TailwindCli.ReuseCached(path, m => said = m);

        // Named, and named with the SIZES: the alternative is a silent re-download that reads like the
        // cache simply missed, on a failure whose whole difficulty was that nothing reported it.
        Assert.Contains(path, said, StringComparison.Ordinal);
        Assert.Contains("bytes where", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Reusing_a_cached_binary_restores_the_executable_bit()
    {
        // The first of the two nightly failures: mode bits lost in the restore, so Exec's /bin/sh exits
        // 126. Reuse has to repair it, or the cache poisons every later build on the machine.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var path = Fetched("tailwindcss-linux-x64", "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Assert.True(TailwindCli.ReuseCached(path));
        Assert.True(File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute));
    }

    // A binary as a completed fetch leaves it: written, then recorded.
    private string Fetched(string name, string contents)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, contents);
        TailwindCli.WriteReceipt(path);
        return path;
    }
}
