using System.Security.Cryptography;
using System.Text;
using Rask.TestFiles;

namespace Rask.Tailwind.Tasks.Tests;

/// <summary>
///     The Tailwind CLI fetch against a loopback release host: a passing 504 is asked again (GitHub's release
///     downloads answered 504 for a morning and failed every cold build), and a cached CLI costs no request at all.
/// </summary>
public sealed class TailwindFetchTests : IDisposable
{
    private static readonly byte[] Cli = Encoding.ASCII.GetBytes("#!/bin/sh\necho tailwindcss\n");

    private readonly string _cache = Path.Combine(Path.GetTempPath(), "rask-tailwind-fetch-" + Path.GetRandomFileName());

    public void Dispose()
    {
        try
        {
            Directory.Delete(_cache, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was fetched (a platform Tailwind publishes no binary for).
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Fact]
    public async Task A_release_host_that_answers_504_twice_is_asked_again_and_the_cli_is_installed()
    {
        await using var host = new FlakyHttpServer(Cli, 504, 504);

        var (resolved, task, engine) = Resolve(host);

        Assert.True(resolved, string.Join('\n', engine.Errors));
        Assert.Equal(3, host.Requests);
        Assert.Equal(Cli, await File.ReadAllBytesAsync(task.ToolPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_cli_already_in_the_cache_is_used_without_a_single_request()
    {
        await using var host = new FlakyHttpServer(Cli);
        var (first, _, _) = Resolve(host);
        Assert.True(first);
        var asked = host.Requests;

        var (resolved, task, _) = Resolve(host);

        Assert.True(resolved);
        Assert.True(File.Exists(task.ToolPath));
        Assert.Equal(asked, host.Requests);
    }

    private (bool Resolved, ResolveTailwindCliTask Task, RecordingEngine Engine) Resolve(FlakyHttpServer host)
    {
        var engine = new RecordingEngine();
        var task = new ResolveTailwindCliTask
        {
            BuildEngine = engine,
            Version = TailwindPins.Version,
            CacheRoot = _cache,
            ExpectedSha256 = Convert.ToHexString(SHA256.HashData(Cli)),
            ReleaseBaseUrl = host.BaseUrl,
            RetryWaits = "0;0",
        };

        return (task.Execute(), task, engine);
    }
}
