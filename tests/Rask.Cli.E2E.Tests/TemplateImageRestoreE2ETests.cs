using System.IO.Compression;

namespace Rask.Cli.E2E.Tests;

/// <summary>
///     Does a template image restore from the project's own feed? Each template's restore steps — the copy before
///     <c>dotnet restore</c> and the restore itself, verbatim, on the template's own SDK stage — run on a project whose
///     one package only its <c>nuget.config</c> names; again with its version given only in
///     <c>Directory.Packages.props</c>; then on a project with neither, which the copy must not fail for (#1307).
/// </summary>
/// <remarks>
///     The feed is a folder in the image rather than a URL, so the check needs no network. The package's id is new on
///     every run: the restore keeps its packages in a cache mount that outlives the image, and a package already there
///     would be found without asking any feed.
/// </remarks>
public sealed class TemplateImageRestoreE2ETests
{
    private const string Copy = """COPY ["Company.RaskServer.csproj",""";

    [Fact]
    public async Task Every_template_image_restores_from_the_projects_nuget_config_and_without_one()
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);
        await DockerAvailable();
        var package = "Rask.Restore.Probe" + Guid.NewGuid().ToString("N")[..8];
        var root = Path.Combine(Path.GetTempPath(), "rask-restore-image", Guid.NewGuid().ToString("N")[..8]);
        var feedOnly = PrivateFeedProject(Path.Combine(root, "feed-only"), package, central: false);
        var withFeed = PrivateFeedProject(Path.Combine(root, "with-feed"), package, central: true);
        var bare = BareProject(Path.Combine(root, "bare"));

        var failures = new List<string>();
        foreach (var template in Templates())
        {
            var name = Path.GetRelativePath(CliBuildE2E.FindRepoRoot(), template);
            var steps = RestoreSteps(File.ReadAllText(template));
            failures.AddRange(await Restore(name, steps, feedOnly, "a feed named only in nuget.config"));
            failures.AddRange(await Restore(name, steps, withFeed, "the project's own feed, with Directory.Packages.props"));
            failures.AddRange(await Restore(name, steps, bare, "a project without nuget.config, Directory.* or global.json"));
        }

        CliBuildE2E.TryDeleteDirectory(root);
        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    private static async Task<IEnumerable<string>> Restore(string template, string steps, string context, string what)
    {
        File.WriteAllText(Path.Combine(context, "Dockerfile"), steps);
        var tag = "rask-restore-image:" + Path.GetFileName(context);

        var (exit, log) = await DeployE2E.RunAsync("docker", ["build", "-t", tag, context]);
        await DeployE2E.RunAsync("docker", ["image", "rm", "-f", tag]);

        return exit == 0 ? [] : [$"{template}: the restore did not succeed for {what}.\n{string.Join('\n', log.Split('\n').TakeLast(30))}"];
    }

    /// <summary>The template's SDK stage, the feed folder a URL stands for, then its restore steps as written.</summary>
    private static string RestoreSteps(string dockerfile)
    {
        var lines = dockerfile.ReplaceLineEndings("\n").Split('\n');
        var from = lines.First(line => line.StartsWith("FROM mcr.microsoft.com/dotnet/sdk", StringComparison.Ordinal));
        var copy = Array.FindIndex(lines, line => line.StartsWith(Copy, StringComparison.Ordinal));
        var restore = Array.FindIndex(lines, copy, line => line.Contains("dotnet restore", StringComparison.Ordinal));
        Assert.True(copy >= 0 && restore > copy, "The template no longer copies its csproj and then restores.");

        return string.Join('\n',
            ["# syntax=docker/dockerfile:1", from, "COPY feed /feed/", "WORKDIR /src", .. lines[copy..(restore + 1)], ""]);
    }

    /// <param name="directory">Where the project goes.</param>
    /// <param name="package">The one package, which only the project's feed has.</param>
    /// <param name="central">
    ///     True to give its version in <c>Directory.Packages.props</c>, with a <c>global.json</c> beside it; false to give
    ///     it in the csproj, so the feed is the only file the restore needs that the csproj is not.
    /// </param>
    private static string PrivateFeedProject(string directory, string package, bool central)
    {
        Directory.CreateDirectory(Path.Combine(directory, "feed"));
        WritePackage(Path.Combine(directory, "feed", $"{package}.1.0.0.nupkg"), package);
        File.WriteAllText(Path.Combine(directory, "Company.RaskServer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="{package}"{(central ? "" : " Version=\"1.0.0\"")} /></ItemGroup>
            </Project>
            """);
        if (!central)
        {
            WriteNuGetConfig(directory);
            return directory;
        }

        File.WriteAllText(Path.Combine(directory, "Directory.Packages.props"), $"""
            <Project>
              <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
              <ItemGroup><PackageVersion Include="{package}" Version="1.0.0" /></ItemGroup>
            </Project>
            """);
        WriteNuGetConfig(directory);
        File.WriteAllText(Path.Combine(directory, "global.json"), """{ "sdk": { "version": "11.0.100", "rollForward": "latestMajor" } }""");
        return directory;
    }

    private static void WriteNuGetConfig(string directory) =>
        File.WriteAllText(Path.Combine(directory, "nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="private" value="/feed" />
              </packageSources>
            </configuration>
            """);

    private static string BareProject(string directory)
    {
        // An empty feed folder: the stage copies one in for every project.
        Directory.CreateDirectory(Path.Combine(directory, "feed"));
        File.WriteAllText(Path.Combine(directory, "feed", ".keep"), "");
        File.WriteAllText(Path.Combine(directory, "Company.RaskServer.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        return directory;
    }

    /// <summary>A package with nothing in it but its manifest: all a restore needs to find it.</summary>
    private static void WritePackage(string path, string id)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry(id + ".nuspec").Open());
        writer.Write($"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>1.0.0</version>
                <authors>Rask</authors>
                <description>Only the project's own feed has this package.</description>
              </metadata>
            </package>
            """);
    }

    private static IEnumerable<string> Templates()
    {
        var templates = Directory.EnumerateFiles(Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates"), "Dockerfile", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains(Copy, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(3, templates.Count);
        return templates;
    }

    private static async Task DockerAvailable()
    {
        var (exit, output) = await DeployE2E.RunAsync("docker", ["version", "--format", "{{.Server.Version}}"]);
        var inCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        Assert.SkipWhen(exit != 0 && !inCi, $"No usable Docker daemon here, so no template image was restored: {output.Trim()}");
        Assert.True(exit == 0, $"CI has no usable Docker daemon for the template restore check: {output}");
    }
}
