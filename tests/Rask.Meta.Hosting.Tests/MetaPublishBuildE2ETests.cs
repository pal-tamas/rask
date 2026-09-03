using System.Diagnostics;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     Drives the shipped <c>build/Rask.Meta.Hosting.props</c> and <c>.targets</c> through a real
///     <c>dotnet publish</c>.
/// </summary>
/// <remarks>
///     <para>
///         A test that read the targets as text could confirm every line and still not notice that the
///         publish copies nothing. That failure is specifically easy to write here: contributing
///         <c>ContentWithTargetPath</c> after <c>ComputeFilesToPublish</c> looks exactly like the
///         working version and silently publishes an empty directory, because the item it derives from
///         was consumed by a target that already ran.
///     </para>
///     <para>
///         So this asserts on the published tree — the artifact — with the front-end build itself
///         stubbed out. What is under test is the wiring, not npm: real framework output is bytes this
///         package never reads, and installing six toolchains to produce them would buy nothing here.
///     </para>
/// </remarks>
public class MetaPublishBuildE2ETests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "rask-meta-pub-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Never created.
        }
        catch (IOException)
        {
            // A file lock on a build output; the temp directory is disposable either way.
        }
    }

    /// <summary>Walks up from the test binaries to the repository root.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void Scaffold(string framework, params string[] frontEndFiles)
    {
        var build = Path.Combine(RepoRoot(), "src", "Rask.Meta.Hosting", "build");

        Write("App.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{Path.Combine(build, "Rask.Meta.Hosting.props")}"/>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <Nullable>enable</Nullable>
                <RaskMetaFramework>{framework}</RaskMetaFramework>
              </PropertyGroup>
              <Import Project="{Path.Combine(build, "Rask.Meta.Hosting.targets")}"/>
            </Project>
            """);

        Write("Program.cs", "System.Console.WriteLine(\"host\");");
        Write("Client/package.json", """{ "name": "front", "private": true }""");

        foreach (var file in frontEndFiles)
        {
            Write("Client/" + file, "// built");
        }
    }

    /// <summary>Publishes with the front-end build skipped, and reports what landed.</summary>
    private string Publish()
    {
        var publishDir = Path.Combine(_dir, "out");

        using var process = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList =
            {
                "publish", Path.Combine(_dir, "App.csproj"),
                "-p:RaskMetaBuild=false",
                "-o", publishDir,
                "-v", "quiet", "--nologo",
            },
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, "dotnet publish failed:\n" + output);
        return publishDir;
    }

    /// <summary>Nitro's whole output directory is published under the front-end directory.</summary>
    [Fact]
    public void A_nitro_build_is_published_beside_the_app()
    {
        Scaffold("nuxt", ".output/server/index.mjs", ".output/public/_nuxt/entry.abc.js");

        var published = Publish();

        Assert.True(File.Exists(Path.Combine(published, "Client", ".output", "server", "index.mjs")));
        Assert.True(File.Exists(
            Path.Combine(published, "Client", ".output", "public", "_nuxt", "entry.abc.js")));
    }

    /// <summary>
    ///     Next's three roots all travel, keeping the layout the development tree has.
    /// </summary>
    /// <remarks>
    ///     Standalone omits <c>public</c> and <c>.next/static</c>, so a publish that copied only the
    ///     standalone directory would produce an app that starts and serves no CSS. Preserving the
    ///     source-relative layout rather than flattening is what lets one <c>AppDirectory</c> default be
    ///     correct in development and in the published app alike.
    /// </remarks>
    [Fact]
    public void Next_publishes_the_server_and_both_asset_roots()
    {
        Scaffold(
            "nextjs",
            ".next/standalone/server.js",
            ".next/static/chunks/main.abc.js",
            "public/robots.txt");

        var published = Publish();

        Assert.True(File.Exists(
            Path.Combine(published, "Client", ".next", "standalone", "server.js")));
        Assert.True(File.Exists(
            Path.Combine(published, "Client", ".next", "static", "chunks", "main.abc.js")));
        Assert.True(File.Exists(Path.Combine(published, "Client", "public", "robots.txt")));
    }

    /// <summary>
    ///     A framework name this package does not know fails the build, by name.
    /// </summary>
    /// <remarks>
    ///     Caught at once rather than as a missing entry file forty seconds into an npm build, which is
    ///     what a typo would otherwise look like.
    /// </remarks>
    [Fact]
    public void An_unknown_framework_name_fails_the_build()
    {
        Scaffold("nextjs");
        Write("App.csproj", File.ReadAllText(Path.Combine(_dir, "App.csproj"))
            .Replace("<RaskMetaFramework>nextjs<", "<RaskMetaFramework>nuxtjs<", StringComparison.Ordinal));

        var publishDir = Path.Combine(_dir, "out");
        using var process = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "publish", Path.Combine(_dir, "App.csproj"), "-o", publishDir, "--nologo" },
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("RASKMETA001", output, StringComparison.Ordinal);
    }
}
