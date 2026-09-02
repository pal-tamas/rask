using System.Globalization;
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

    /// <summary>
    ///     The islands build floor is the SPA build floor. Both run vite, so both take vite's answer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>RaskExternalMinimumNode</c> spent its whole life declared and unread — the property was
    ///         set, its comment promised a probe, and <c>Rask.External.targets</c> referenced it nowhere, so
    ///         an old Node went straight to <c>npm</c> and failed inside vite with the engines error the
    ///         probe existed to replace. Now that <c>_RaskExternalProbeNode</c> enforces it as
    ///         RASKISLAND001, the number matters, and two files stating it is two places to get it wrong.
    ///     </para>
    ///     <para>
    ///         They must agree because they are the same requirement: vite's
    ///         <c>^20.19.0 || &gt;=22.12.0</c>, whose disjoint shape a single numeric floor cannot express.
    ///         22.12.0 is the lowest version with no hole beneath it.
    ///     </para>
    /// </remarks>
    [Fact]
    public void The_islands_floor_matches_the_spa_floor()
    {
        var islands = Regex.Match(
            RepoPins.Text("src/Rask.External/build/Rask.External.props"),
            @"<RaskExternalMinimumNode[^>]*>([0-9.]+)</RaskExternalMinimumNode>");
        Assert.True(islands.Success, "RaskExternalMinimumNode is no longer declared in Rask.External.props");

        Assert.Equal(NodeRequirement.BuildFloor, Version.Parse(islands.Groups[1].Value));
    }

    /// <summary>
    ///     Both installers leave an existing Node alone at exactly the scaffold line, not at some other
    ///     number that happens to be near it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>RASK_INSTALL_NODE_MIN</c> decides whether <c>rask.sh</c> installs Node or keeps the one
    ///         already on the box, and the thing that Node must be able to do is SCAFFOLD — so it is the
    ///         scaffold line, not the build floor. The two installers state it independently, in two
    ///         languages, and <c>rask.ps1</c>'s comment says it mirrors <c>rask.sh</c>. Nothing checked that
    ///         it does, so a bump applied to one and forgotten on the other would leave Windows users on a
    ///         Node that installs cleanly and then cannot run <c>rask new --template angular</c>.
    ///     </para>
    ///     <para>
    ///         Note what is deliberately NOT asserted here: the several places that quote Angular's own
    ///         range, <c>^22.22.3 || ^24.15.0 || &gt;=26.0.0</c>. That string contains 24.15.0 by
    ///         coincidence — it is a fact about somebody else's CLI, and it must NOT be rewritten when
    ///         Rask's line moves.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Both_installers_state_the_scaffold_line()
    {
        var shell = Regex.Match(
            RepoPins.Text("rask.sh"),
            """RASK_INSTALL_NODE_MIN="\$\{RASK_INSTALL_NODE_MIN:-([0-9.]+)\}""");
        Assert.True(shell.Success, "rask.sh no longer defaults RASK_INSTALL_NODE_MIN.");
        Assert.Equal(NodeRequirement.ScaffoldLine, Version.Parse(shell.Groups[1].Value));

        var powershell = Regex.Match(
            RepoPins.Text("rask.ps1"),
            @"\$NodeMin\s*=.*?else\s*\{\s*'([0-9.]+)'\s*\}");
        Assert.True(powershell.Success, "rask.ps1 no longer defaults $NodeMin.");
        Assert.Equal(NodeRequirement.ScaffoldLine, Version.Parse(powershell.Groups[1].Value));
    }

    /// <summary>
    ///     The installation docs state the same Node line the installers enforce.
    /// </summary>
    /// <remarks>
    ///     Docs are the half that rots silently: nothing builds them, so a bump lands in the shell scripts
    ///     and leaves the table telling people a version the installer no longer agrees with. Both places
    ///     checked here state RASK'S OWN line — the environment-variable default table and the summary of
    ///     what the installer puts on the box.
    /// </remarks>
    [Fact]
    public void The_installation_docs_state_the_scaffold_line()
    {
        var docs = RepoPins.Text("docs/installation.md");

        var tabled = Regex.Match(docs, @"RASK_INSTALL_NODE_MIN`\s*\|\s*`([0-9.]+)`");
        Assert.True(tabled.Success, "docs/installation.md no longer tables the RASK_INSTALL_NODE_MIN default.");
        Assert.Equal(NodeRequirement.ScaffoldLine, Version.Parse(tabled.Groups[1].Value));

        var summarised = Regex.Match(docs, @"Node (\d+) LTS");
        Assert.True(summarised.Success, "docs/installation.md no longer names the Node LTS line it installs.");
        Assert.Equal(
            NodeRequirement.ScaffoldLine.Major,
            int.Parse(summarised.Groups[1].Value, CultureInfo.InvariantCulture));
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
