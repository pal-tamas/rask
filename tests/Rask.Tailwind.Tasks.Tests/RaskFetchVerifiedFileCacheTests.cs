using System.Security.Cryptography;
using System.Text;
using Rask.Build;
using Rask.TestFiles;

namespace Rask.Tailwind.Tasks.Tests;

// A file already at the destination used to be taken as the pinned one unread, and an unpinned file a build cannot
// do without was fetched and run with a warning only.
public sealed class RaskFetchVerifiedFileCacheTests : IDisposable
{
    private static readonly byte[] Binary = Encoding.ASCII.GetBytes("#!/bin/sh\necho tool\n");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rask-fetch-cache-" + Path.GetRandomFileName());

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written.
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Fact]
    public async Task A_file_in_place_that_is_not_the_pinned_one_is_fetched_again()
    {
        await using var host = new FlakyHttpServer(Binary);
        var destination = Path.Combine(_dir, "tool");
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(destination, "tampered", TestContext.Current.CancellationToken);

        var (ok, engine) = Run(host.BaseUrl + "/tool", destination, Pin(Binary));

        Assert.True(ok);
        Assert.Equal(1, host.Requests);
        Assert.Equal(Binary, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.Contains(engine.Messages, static w => w.Contains("not the pinned", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unpinned_file_the_build_needs_is_an_error_and_never_fetched()
    {
        await using var host = new FlakyHttpServer(Binary);
        var destination = Path.Combine(_dir, "tool");

        var (ok, engine) = Run(host.BaseUrl + "/tool", destination, sha256: string.Empty);

        Assert.False(ok);
        Assert.Equal(0, host.Requests);
        Assert.Contains(engine.Errors, static e => e.Contains("no SHA-256 is pinned", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unpinned_file_the_build_can_go_without_is_still_only_a_warning()
    {
        await using var host = new FlakyHttpServer(Binary);
        var destination = Path.Combine(_dir, "tool");

        var (ok, engine) = Run(host.BaseUrl + "/tool", destination, sha256: string.Empty, required: false);

        Assert.True(ok);
        Assert.Empty(engine.Errors);
        Assert.True(File.Exists(destination));
    }

    private static string Pin(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static (bool Ok, RecordingEngine Engine) Run(string url, string destination, string sha256, bool required = true)
    {
        var engine = new RecordingEngine();
        var task = new RaskFetchVerifiedFile
        {
            BuildEngine = engine,
            Url = url,
            DestinationFile = destination,
            Sha256 = sha256,
            Product = "Test",
            Required = required,
            RetryWaits = "0;0",
        };

        return (task.Execute(), engine);
    }
}
