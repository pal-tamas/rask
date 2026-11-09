using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Rask.TestFiles;

namespace Rask.Pdf.Tests;

/// <summary>
///     The build's Chrome for Testing fetch (build/Rask.Pdf.targets) against a loopback host: a cached shell
///     costs no request, and a host answering 504 is asked again rather than failing the build.
/// </summary>
/// <remarks>
///     Real MSBuild over the real targets, in a throwaway project that names itself an app with the package, so
///     each case costs an evaluation and the inline fetch task, not a restore and a compile.
/// </remarks>
public sealed class ChromiumBuildFetchTests : IDisposable
{
    private const string Version = "999.0.0.0";

    private static readonly string[] Platforms = ["linux64", "linux-arm64", "mac-arm64", "mac-x64", "win64"];

    private static readonly byte[] Archive = Zip();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rask-pdf-build-" + Guid.NewGuid().ToString("n"));

    private string Cache => Path.Combine(_root, "cache");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    [Fact]
    public async Task A_host_that_answers_504_twice_is_asked_again_and_the_shell_is_unpacked_and_marked_verified()
    {
        await using var host = new FlakyHttpServer(Archive, 504, 504);

        var result = await Fetch(host);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(3, host.Requests);
        Assert.Single(Directory.GetFiles(Cache, ".verified", SearchOption.AllDirectories));
        Assert.Contains(
            Directory.GetFiles(Cache, "chrome-headless-shell*", SearchOption.AllDirectories),
            path => Path.GetFileName(path).StartsWith("chrome-headless-shell", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_shell_already_in_the_cache_is_used_without_a_single_request()
    {
        await using var warm = new FlakyHttpServer(Archive);
        Assert.True((await Fetch(warm)).ExitCode == 0);
        await using var host = new FlakyHttpServer(Archive, 504, 504, 504);

        var result = await Fetch(host);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(0, host.Requests);
    }

    private async Task<TestProcessResult> Fetch(FlakyHttpServer host)
    {
        var build = Path.Combine(RepositoryRoot(), "src", "Rask.Pdf", "build").Replace('\\', '/');
        var sha = Convert.ToHexString(SHA256.HashData(Archive)).ToLowerInvariant();
        var project = Path.Combine(_root, "probe.proj");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(project, $"""
            <Project>
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <RaskPdfPackaged>true</RaskPdfPackaged>
                <RaskPdfChromeVersion>{Version}</RaskPdfChromeVersion>
                <RaskPdfChromeSha256_linux64>{sha}</RaskPdfChromeSha256_linux64>
                <RaskPdfChromeSha256_linux_arm64>{sha}</RaskPdfChromeSha256_linux_arm64>
                <RaskPdfChromeSha256_mac_arm64>{sha}</RaskPdfChromeSha256_mac_arm64>
                <RaskPdfChromeSha256_mac_x64>{sha}</RaskPdfChromeSha256_mac_x64>
                <RaskPdfChromeSha256_win64>{sha}</RaskPdfChromeSha256_win64>
                <RaskPdfChromeBaseUrl>{host.BaseUrl}</RaskPdfChromeBaseUrl>
                <RaskPdfChromiumCache>{Cache}</RaskPdfChromiumCache>
                <_RaskFetchRetryWaits>0;0</_RaskFetchRetryWaits>
              </PropertyGroup>
              <Target Name="Build" />
              <Import Project="{build}/Rask.Pdf.targets" />
            </Project>
            """, TestContext.Current.CancellationToken);

        // nodeReuse off: a persisted node would hold this temp directory open and outlive the test.
        return await TestProcess.Run(
            "dotnet",
            ["msbuild", project, "-t:Build", "-nologo", "-nodeReuse:false", "-v:m"],
            cancellationToken: TestContext.Current.CancellationToken);
    }

    // What a Chrome for Testing archive holds, for whichever platform this machine is: the shell in a folder named
    // after the platform.
    private static byte[] Zip()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var platform in Platforms)
            {
                using var entry = zip.CreateEntry($"chrome-headless-shell-{platform}/chrome-headless-shell").Open();
                entry.Write(Encoding.ASCII.GetBytes("#!/bin/sh\necho chrome\n"));
            }
        }

        return buffer.ToArray();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
