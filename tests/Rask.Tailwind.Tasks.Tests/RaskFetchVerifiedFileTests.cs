using System.Security.Cryptography;
using System.Text;
using Rask.Build;
using Rask.TestFiles;

namespace Rask.Tailwind.Tasks.Tests;

/// <summary>
///     The one fetch every native binary goes through (src/Rask.Build.Shared): a cached file costs no request, a
///     release host's passing 504 is ridden out, and bytes that are not the pinned ones never land.
/// </summary>
/// <remarks>
///     Here because Rask.Tailwind.Tasks compiles the shared file in; Rask.SQLite.Litestream.Tests and
///     Rask.Pdf.Tests drive the same file as the inline task an app's build compiles.
/// </remarks>
public sealed class RaskFetchVerifiedFileTests : IDisposable
{
    private static readonly byte[] Binary = Encoding.ASCII.GetBytes("#!/bin/sh\necho litestream\n");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rask-fetch-" + Path.GetRandomFileName());

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
    public async Task A_file_already_in_place_is_used_without_a_single_request()
    {
        await using var host = new FlakyHttpServer(Binary);
        var destination = Path.Combine(_dir, "tool");
        Directory.CreateDirectory(_dir);
        await File.WriteAllBytesAsync(destination, Binary, TestContext.Current.CancellationToken);

        var (ok, task, _) = Run(host.BaseUrl + "/tool", destination);

        Assert.True(ok);
        Assert.True(task.Fetched);
        Assert.Equal(0, host.Requests);
    }

    [Fact]
    public async Task A_host_that_answers_504_twice_is_asked_again_and_the_third_answer_is_installed()
    {
        await using var host = new FlakyHttpServer(Binary, 504, 504);
        var destination = Path.Combine(_dir, "tool");

        var (ok, task, engine) = Run(host.BaseUrl + "/tool", destination);

        Assert.True(ok, string.Join('\n', engine.Errors));
        Assert.True(task.Fetched);
        Assert.Equal(3, host.Requests);
        Assert.Equal(Binary, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.Equal([destination], Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Bytes_that_are_not_the_pinned_ones_fail_the_build_and_leave_nothing_behind()
    {
        await using var host = new FlakyHttpServer(Encoding.ASCII.GetBytes("a replaced release asset"));
        var destination = Path.Combine(_dir, "tool");

        var (ok, task, engine) = Run(host.BaseUrl + "/tool", destination, required: false);

        Assert.False(ok);
        Assert.False(task.Fetched);
        Assert.Contains(engine.Errors, e => e.Contains("not the pinned", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task A_host_that_never_recovers_fails_a_build_that_needs_the_file_naming_the_cache_to_keep()
    {
        await using var host = new FlakyHttpServer(Binary, 504, 504, 504);
        var destination = Path.Combine(_dir, "tool");

        var (ok, _, engine) = Run(host.BaseUrl + "/tool", destination);

        Assert.False(ok);
        Assert.Equal(3, host.Requests);
        var error = Assert.Single(engine.Errors);
        Assert.Contains("after 3 attempts", error, StringComparison.Ordinal);
        Assert.Contains("caching-the-builds-tools-in-ci", error, StringComparison.Ordinal);
        Assert.Contains(_dir, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_that_never_recovers_is_only_a_warning_for_a_build_that_can_go_on_without_the_file()
    {
        await using var host = new FlakyHttpServer(Binary, 503, 503, 503);
        var destination = Path.Combine(_dir, "tool");

        var (ok, task, engine) = Run(host.BaseUrl + "/tool", destination, required: false);

        Assert.True(ok);
        Assert.False(task.Fetched);
        Assert.Empty(engine.Errors);
        Assert.Contains(engine.Messages, m => m.Contains("could not download", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_file_the_host_does_not_have_is_not_asked_for_again()
    {
        await using var host = new FlakyHttpServer(Binary, 404);
        var destination = Path.Combine(_dir, "tool");

        var (ok, _, _) = Run(host.BaseUrl + "/tool", destination);

        Assert.False(ok);
        Assert.Equal(1, host.Requests);
    }

    private static (bool Ok, RaskFetchVerifiedFile Task, RecordingEngine Engine) Run(
        string url, string destination, bool required = true)
    {
        var engine = new RecordingEngine();
        var task = new RaskFetchVerifiedFile
        {
            BuildEngine = engine,
            Url = url,
            DestinationFile = destination,
            Sha256 = Convert.ToHexString(SHA256.HashData(Binary)),
            Product = "Test",
            Required = required,
            RetryWaits = "0;0",
        };

        return (task.Execute(), task, engine);
    }
}
