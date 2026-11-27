namespace Rask.Cli.Tests;

/// <summary>
///     Rask requires C# 15: <c>Rask.Core.targets</c> stops a project that pins an older <c>LangVersion</c> with
///     <c>RASKLANG001</c>, and lets through everything that resolves to C# 15 or later on the .NET 11 SDK.
/// </summary>
/// <remarks>
///     Run by MSBuild itself against the shipped targets — the claim is about how MSBuild reads the property,
///     which only an evaluation shows. One <c>dotnet msbuild</c> per case, running that one target.
/// </remarks>
public sealed class LanguageVersionRequirementTests
{
    [Theory]
    [InlineData("14")]
    [InlineData("13.0")]
    [InlineData("7.3")]
    [InlineData("ISO-2")]
    public async Task A_language_version_below_15_fails_the_build_naming_it(string version)
    {
        var (exitCode, output) = await RunCheck($"-p:LangVersion={version}");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("RASKLANG001", output, StringComparison.Ordinal);
        Assert.Contains($"<LangVersion>{version}</LangVersion>", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("15")]
    [InlineData("15.0")]
    [InlineData("latest")]
    [InlineData("latestMajor")]
    [InlineData("preview")]
    [InlineData("default")]
    public async Task A_language_version_of_15_or_later_builds(string version)
    {
        var (exitCode, output) = await RunCheck($"-p:LangVersion={version}");

        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public async Task An_unset_language_version_builds()
    {
        var (exitCode, output) = await RunCheck();

        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public async Task A_netstandard_project_is_not_checked_for_the_7_3_its_sdk_pins()
    {
        var (exitCode, output) = await RunCheck("-p:TargetFrameworkIdentifier=.NETStandard", "-p:LangVersion=7.3");

        Assert.True(exitCode == 0, output);
    }

    private static async Task<(int ExitCode, string Output)> RunCheck(params string[] extra)
    {
        var targets = Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Core", "build", "Rask.Core.targets");
        var directory = Directory.CreateTempSubdirectory("rask-langversion-");

        try
        {
            var project = Path.Combine(directory.FullName, "Probe.proj");
            await File.WriteAllTextAsync(
                project,
                $"""
                 <Project>
                   <PropertyGroup><Language>C#</Language><TargetFrameworkIdentifier>.NETCoreApp</TargetFrameworkIdentifier></PropertyGroup>
                   <Import Project="{targets}" />
                 </Project>
                 """);

            var result = await TestProcess.Run(
                "dotnet",
                ["msbuild", project, "-nologo", "-t:_RaskRequireCSharp15", .. extra],
                directory.FullName);

            return (result.ExitCode, result.Output);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
