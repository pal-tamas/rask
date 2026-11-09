using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Rask.TestFiles;

namespace Rask.SQLite.Litestream.Tests;

/// <summary>
///     The build's half of the package: which apps get the litestream binary bundled, and what a release host's
///     outage does to their build. Real MSBuild over the real props and targets, against a loopback host.
/// </summary>
/// <remarks>
///     <para>
///         The bug this guards: Rask.Server depends on this package, and its buildTransitive targets fetched the
///         binary from GitHub on EVERY build of EVERY server app — a SQL Server-only app included — so GitHub's
///         release downloads answering 504 failed a build that had no use for the file (MSB3923).
///     </para>
///     <para>
///         A throwaway project imports the props and targets and stands in for the SDK's AssignTargetPaths and
///         PrepareForPublish, so each case costs an evaluation and the inline fetch task, not a restore and a
///         compile.
///     </para>
/// </remarks>
public sealed class LitestreamBuildFetchTests : IDisposable
{
    private const string Version = "9.9.9";
    private const string Rid = "linux-x64";

    private static readonly byte[] Binary = Encoding.ASCII.GetBytes("#!/bin/sh\necho litestream\n");
    private static readonly byte[] Archive = TarGz("litestream", Binary);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rask-litestream-build-" + Guid.NewGuid().ToString("n"));

    private string Cache => Path.Combine(_root, "cache");

    private string CachedBinary => Path.Combine(Cache, Version, Rid, "litestream");

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
    public async Task An_app_that_has_the_package_only_through_Rask_Server_asks_nothing_of_a_host_that_is_down()
    {
        await using var host = new FlakyHttpServer(Archive, 504, 504, 504, 504);

        var result = await Build(host, reference: false, "-t:Publish", "-p:_IsPublishing=true");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(0, host.Requests);
        Assert.DoesNotContain("Rask.SQLite.Litestream", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_that_references_the_package_still_builds_while_the_host_is_down_with_a_warning()
    {
        await using var host = new FlakyHttpServer(Archive, 504, 504, 504);

        var result = await Build(host, reference: true, "-t:Build");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(3, host.Requests);
        Assert.Contains("warning", result.Output, StringComparison.Ordinal);
        Assert.Contains("could not download", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(CachedBinary));
    }

    [Fact]
    public async Task An_app_that_references_the_package_fails_to_publish_while_the_host_is_down_naming_the_cache()
    {
        await using var host = new FlakyHttpServer(Archive, 504, 504, 504);

        var result = await Build(host, reference: true, "-t:Publish", "-p:_IsPublishing=true");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error", result.Output, StringComparison.Ordinal);
        Assert.Contains(Cache, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_that_references_the_package_publishes_a_verified_binary_after_two_504s()
    {
        await using var host = new FlakyHttpServer(Archive, 504, 504);

        var result = await Build(host, reference: true, "-t:Publish", "-p:_IsPublishing=true");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(3, host.Requests);
        Assert.Equal(Binary, await File.ReadAllBytesAsync(CachedBinary, TestContext.Current.CancellationToken));
        Assert.Contains("bundled: litestream", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_binary_already_in_the_cache_is_bundled_without_a_single_request()
    {
        await using var host = new FlakyHttpServer(Archive, 504, 504, 504);
        Directory.CreateDirectory(Path.GetDirectoryName(CachedBinary)!);
        await File.WriteAllBytesAsync(CachedBinary, Binary, TestContext.Current.CancellationToken);

        var result = await Build(host, reference: true, "-t:Publish", "-p:_IsPublishing=true");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(0, host.Requests);
        Assert.Contains("bundled: litestream", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RaskLitestreamDownload_false_fetches_nothing_even_for_an_app_that_references_the_package()
    {
        await using var host = new FlakyHttpServer(Archive);

        var result = await Build(host, reference: true, "-t:Publish", "-p:_IsPublishing=true", "-p:RaskLitestreamDownload=false");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(0, host.Requests);
        Assert.DoesNotContain("bundled: litestream", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RaskLitestreamDownload_true_bundles_the_binary_for_an_app_that_has_the_package_only_through_Rask_Server()
    {
        await using var host = new FlakyHttpServer(Archive);

        var result = await Build(host, reference: false, "-t:Build", "-p:RaskLitestreamDownload=true");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(1, host.Requests);
        Assert.Contains("bundled: litestream", result.Output, StringComparison.Ordinal);
    }

    private async Task<TestProcessResult> Build(FlakyHttpServer host, bool reference, params string[] arguments)
    {
        var build = Path.Combine(RepositoryRoot(), "src", "Rask.SQLite.Litestream", "build").Replace('\\', '/');
        var project = Path.Combine(_root, "probe.proj");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(project, $"""
            <Project>
              <PropertyGroup>
                <RuntimeIdentifier>{Rid}</RuntimeIdentifier>
                <RaskLitestreamVersion>{Version}</RaskLitestreamVersion>
                <RaskLitestreamSha256>{Convert.ToHexString(SHA256.HashData(Archive))}</RaskLitestreamSha256>
                <RaskLitestreamReleaseBaseUrl>{host.BaseUrl}</RaskLitestreamReleaseBaseUrl>
                <RaskLitestreamCacheRoot>{Cache}</RaskLitestreamCacheRoot>
                <_RaskFetchRetryWaits>0;0</_RaskFetchRetryWaits>
                <TargetDir>{_root}/bin/</TargetDir>
                <PublishDir>{_root}/publish/</PublishDir>
              </PropertyGroup>
              <Import Project="{build}/Rask.SQLite.Litestream.props" />
              <ItemGroup>
                {(reference ? """<PackageReference Include="Rask.SQLite.Litestream" Version="1.0.0" />""" : """<PackageReference Include="Rask.Server" Version="1.0.0" />""")}
              </ItemGroup>
              <Target Name="AssignTargetPaths" />
              <Target Name="PrepareForPublish" />
              <Target Name="Build" DependsOnTargets="AssignTargetPaths;Report" />
              <Target Name="Publish" DependsOnTargets="AssignTargetPaths;PrepareForPublish;Report" />
              <Target Name="Report">
                <Message Importance="high" Text="bundled: %(None.Link)" />
              </Target>
              <Import Project="{build}/Rask.SQLite.Litestream.targets" />
            </Project>
            """, TestContext.Current.CancellationToken);

        // nodeReuse off: a persisted node would hold this temp directory open and outlive the test.
        return await TestProcess.Run(
            "dotnet",
            ["msbuild", project, "-nologo", "-nodeReuse:false", "-v:m", .. arguments],
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static byte[] TarGz(string name, byte[] content)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(content),
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            });
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
