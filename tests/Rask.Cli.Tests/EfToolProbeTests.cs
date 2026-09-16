using System.Xml.Linq;

namespace Rask.Cli.Tests;

/// <summary>
///     The EF Core tools floor, and the version parsing that decides whether the installed tool is under
///     it.
/// </summary>
/// <remarks>
///     The floor is stated twice — once in <c>Directory.Packages.props</c>, which is what a scaffolded app
///     restores, and once in <see cref="EfToolProbe.Floor" />, which is what the CLI checks against. Two
///     copies of one number drift silently, so this reads the props file and compares. Same arrangement as
///     <c>NodeRequirementTests</c>.
/// </remarks>
public sealed class EfToolProbeTests
{
    [Fact]
    public void The_floor_is_the_EF_Core_version_the_repo_pins()
    {
        var props = XDocument.Load(RepoFile("Directory.Packages.props"));

        var pinned = props.Descendants("PackageVersion")
            .Single(e => (string?)e.Attribute("Include") == "Microsoft.EntityFrameworkCore")
            .Attribute("Version")!.Value;

        Assert.Equal(
            Version.Parse(pinned),
            EfToolProbe.Floor);
    }

    [Theory]
    // The real shape: a banner line, then the version.
    [InlineData("Entity Framework Core .NET Command-line Tools\n10.0.5\n", "10.0.5")]
    [InlineData("10.0.12", "10.0.12")]
    // A prerelease tool is compared on its release part, so 11.0.0-rc.1 is not read as older than 11.0.0.
    [InlineData("11.0.0-rc.1.25451.107\n", "11.0.0")]
    [InlineData("  10.0.5  \n\n", "10.0.5")]
    public async Task The_version_is_read_off_the_last_line_that_is_one(string output, string expected)
    {
        var process = new FakeProcessRunner { CaptureResult = new ProcessResult(0, output, "") };

        Assert.Equal(
            Version.Parse(expected),
            await EfToolProbe.InstalledVersionAsync(process, CancellationToken.None));
    }

    [Fact]
    public async Task A_non_zero_exit_means_not_installed()
    {
        var process = new FakeProcessRunner { CaptureResult = new ProcessResult(1, "", "not found") };

        Assert.Null(await EfToolProbe.InstalledVersionAsync(process, CancellationToken.None));
    }

    [Fact]
    public async Task Output_that_names_no_version_is_installed_but_unknown()
    {
        // Installed-but-unknown, never "missing": reinstalling a tool that is demonstrably there, because
        // its banner changed shape, is worse than leaving it alone.
        var process = new FakeProcessRunner
        {
            CaptureResult = new ProcessResult(0, "Entity Framework Core .NET Command-line Tools\n", ""),
        };

        Assert.Equal(
            EfToolProbe.UnknownVersion,
            await EfToolProbe.InstalledVersionAsync(process, CancellationToken.None));
    }

    private static string RepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relative);
    }
}
