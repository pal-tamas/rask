using System.Text.RegularExpressions;
using Rask.Cli;

namespace Rask.Cli.Tests;

/// <summary>
///     The two Node numbers the CLI states, and the one that is not allowed to drift.
/// </summary>
public sealed class NodeRequirementTests
{
    /// <summary>
    ///     <see cref="NodeRequirement.BuildFloor" /> mirrors <c>RaskSpaMinimumNode</c>; the props file is
    ///     the enforcing copy.
    /// </summary>
    /// <remarks>
    ///     Asserted against the SHIPPED props rather than a constant repeated in the test, because a copy
    ///     of a number in a test is a third place for it to be wrong. The build floor is enforced by
    ///     MSBuild as RASKSPA005 and by nothing in the CLI, so if these two disagree the CLI tells people
    ///     to install a Node that the build then rejects — or, worse, accepts one it will not.
    /// </remarks>
    [Fact]
    public void The_build_floor_is_the_one_the_props_file_enforces()
    {
        var props = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Rask.Spa.Hosting", "build", "Rask.Spa.Hosting.props"));

        var declared = Regex.Match(props, @"<RaskSpaMinimumNode[^>]*>([0-9.]+)</RaskSpaMinimumNode>");
        Assert.True(declared.Success, "RaskSpaMinimumNode is no longer declared in Rask.Spa.Hosting.props");

        Assert.Equal(NodeRequirement.BuildFloor, Version.Parse(declared.Groups[1].Value));
    }

    /// <summary>
    ///     The scaffold line is ABOVE the build floor, which is the whole point of having two numbers.
    /// </summary>
    [Fact]
    public void The_scaffold_line_is_higher_than_the_build_floor()
    {
        Assert.True(
            NodeRequirement.ScaffoldLine > NodeRequirement.BuildFloor,
            $"the scaffold line ({NodeRequirement.ScaffoldLine}) must exceed the build floor "
            + $"({NodeRequirement.BuildFloor}) — an app builds on less than it takes to scaffold one.");
    }

    /// <summary>
    ///     It also has to clear the floor the Angular CLI enforces for itself, which is what #886 hit.
    /// </summary>
    [Fact]
    public void The_scaffold_line_clears_the_angular_cli_floor()
    {
        // Angular's CLI refuses below ^22.22.3 || ^24.15.0 || >=26.0.0. A machine on the 24 line has to
        // be at 24.15.0 to satisfy it, which is exactly the version that turned the CLI build gate red
        // on a box running 24.14.0.
        Assert.True(NodeRequirement.ScaffoldLine >= new Version(24, 15, 0));
    }

    [Theory]
    // node prints a leading v.
    [InlineData("v24.20.0", "24.20.0")]
    [InlineData("24.20.0", "24.20.0")]
    // npm does not.
    [InlineData("11.19.0", "11.19.0")]
    // A pre-release SDK must not parse as "absent" — that would report a present tool as missing.
    [InlineData("10.0.100-preview.3.25201.16", "10.0.100")]
    [InlineData("10.0.100+abc123", "10.0.100")]
    // A bare major is padded rather than discarded.
    [InlineData("24", "24.0")]
    [InlineData("  v24.20.0  ", "24.20.0")]
    public void A_reported_version_is_read_the_way_each_tool_prints_it(string reported, string expected) =>
        Assert.Equal(Version.Parse(expected), NodeRequirement.Parse(reported));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a version")]
    [InlineData("v")]
    public void Anything_unparseable_is_null_rather_than_a_wrong_number(string? reported) =>
        Assert.Null(NodeRequirement.Parse(reported));

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
