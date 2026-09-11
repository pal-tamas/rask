using System.Text.RegularExpressions;
using static Rask.External.Tests.IslandBuild;

namespace Rask.External.Tests;

/// <summary>
///     A package island through real MSBuild: found before the compile, joined to the island list, checked for a
///     snapshot when nothing can extract one, and handed to the generators as exactly one additional file.
/// </summary>
/// <remarks>
///     <para>
///         Driven through the shipped build assets for the reason <see cref="IslandScopedTsSeparationTests" />
///         gives: the subject is the order of targets across <c>Rask.Core.targets</c> and
///         <c>Rask.External.targets</c>, and a test over a task alone proves nothing about that. Two of these
///         guarantees rest on MSBuild evaluating a target's Condition before its dependencies run.
///     </para>
///     <para>
///         No node and no npm: every case is one where the props cannot be extracted, which is also where a
///         committed snapshot is the only thing standing between the author and an island with no props.
///     </para>
/// </remarks>
public sealed class PackageIslandTargetsTests
{
    // The override sits on line 5, which is where RASKISLAND006 has to point.
    private const string Island =
        "namespace Shop;\n\npublic sealed partial class MuiButton : Rask.External.ReactComponent\n{\n"
        + "    protected override string Module => \"@mui/material#Button\";\n}\n";

    private const string VueIsland =
        "namespace Shop;\n\npublic sealed partial class Toggle : Rask.External.VueComponent\n{\n"
        + "    protected override string Module => \"@acme/toggle\";\n}\n";

    private const string Snapshot =
        "{\n  \"schema\": 1,\n  \"runtime\": \"react\",\n  \"module\": \"@mui/material\",\n  \"export\": \"Button\",\n"
        + "  \"package\": {\n    \"name\": \"@mui/material\",\n    \"version\": \"7.3.1\"\n  },\n  \"props\": []\n}\n";

    [Fact]
    public async Task A_package_island_is_found_before_the_compile_and_joins_the_island_list()
    {
        var (exit, output) = await Build(packageJson: true, snapshot: true, "-t:ReportPackageIslands");

        Assert.True(exit == 0, output);
        Assert.Contains("PACKAGE=MuiButton|react|@mui/material#Button", output, StringComparison.Ordinal);

        // Joined to @(_RaskExternalFile) as its snapshot, carrying the module the entry imports.
        Assert.Contains("FILE=MuiButton.props.json|@mui/material#Button", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_build_that_cannot_extract_names_the_missing_snapshot_at_the_Module_line()
    {
        var (exit, output) = await Build(
            packageJson: true, snapshot: false, "-t:_RaskExternalPackageProps -p:RaskExternalBuild=false");

        Assert.NotEqual(0, exit);
        Assert.Contains("RASKISLAND006", output, StringComparison.Ordinal);
        Assert.Contains("MuiButton.cs(5", output, StringComparison.Ordinal);
        Assert.Contains("RaskExternalBuild=false", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_committed_snapshot_is_compiled_from_when_nothing_can_extract()
    {
        var (exit, output) = await Build(
            packageJson: true, snapshot: true, "-t:_RaskExternalPackageProps;ReportAdditionalFiles -p:RaskExternalBuild=false");

        Assert.True(exit == 0, output);

        // Once: the evaluation glob found it, and _RaskExternalAddSnapshots must replace that item, not add a second.
        Assert.Single(Regex.Matches(output, @"ADDITIONAL=MuiButton\.props\.json"));
    }

    [Fact]
    public async Task A_project_with_no_package_json_still_hands_its_snapshot_to_the_generators()
    {
        var (exit, output) = await Build(packageJson: false, snapshot: true, "-t:ReportPackageIslands;ReportAdditionalFiles");

        Assert.True(exit == 0, output);

        // No scan without a package.json, and the committed file still reaches the compile.
        Assert.DoesNotContain("PACKAGE=", output, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(output, @"ADDITIONAL=MuiButton\.props\.json"));
    }

    [Fact]
    public async Task A_runtime_whose_packages_are_not_read_yet_is_held_to_its_snapshot_instead()
    {
        // Extraction is possible here, and still a Vue package island is not sent to the extractor, which would
        // fail it (RASKISLAND007) on every build. It is held to its committed snapshot, which is missing.
        var (exit, output) = await Build(
            packageJson: true, snapshot: false, "-t:_RaskExternalPackageProps", source: VueIsland, name: "Toggle");

        Assert.NotEqual(0, exit);
        Assert.Contains("RASKISLAND006", output, StringComparison.Ordinal);
        Assert.Contains("React, Preact and Solid", output, StringComparison.Ordinal);
        Assert.DoesNotContain("RASKISLAND007", output, StringComparison.Ordinal);
    }

    private static async Task<(int Exit, string Output)> Build(
        bool packageJson, bool snapshot, string arguments, string source = Island, string name = "MuiButton")
    {
        var src = Path.Combine(RepoRoot(), "src");
        var temp = Path.Combine(Path.GetTempPath(), "rask-package-island", Guid.NewGuid().ToString("N"));
        var shop = Directory.CreateDirectory(Path.Combine(temp, "Shop")).FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(shop, name + ".cs"), source);
            if (snapshot)
            {
                await File.WriteAllTextAsync(Path.Combine(shop, name + ".props.json"), Snapshot);
            }

            if (packageJson)
            {
                await File.WriteAllTextAsync(Path.Combine(temp, "package.json"), """{ "private": true }""");
            }

            // A bare project needs no restore and reaches no network; ResolveProjectReferences is stubbed because
            // there is no reference to resolve and the task assembly is already on disk.
            var project = Path.Combine(temp, "package-island.proj");
            await File.WriteAllTextAsync(
                project,
                $"""
                 <Project>
                   <Import Project="{Path.Combine(src, "Rask.External", "build", "Rask.External.props")}"/>
                   <Import Project="{Path.Combine(src, "Rask.Core", "build", "Rask.Core.targets")}"/>
                   <Import Project="{Path.Combine(src, "Rask.External", "build", "Rask.External.targets")}"/>
                   <ItemGroup>
                     <Compile Include="**\*.cs"/>
                   </ItemGroup>
                   <Target Name="ResolveProjectReferences"/>
                   <Target Name="ReportPackageIslands" DependsOnTargets="_RaskExternalFindPackageIslands">
                     <Message Importance="high" Condition="'%(_RaskExternalPackageIsland.IslandName)' != ''"
                              Text="PACKAGE=%(_RaskExternalPackageIsland.IslandName)|%(_RaskExternalPackageIsland.Runtime)|%(_RaskExternalPackageIsland.PackageModule)"/>
                     <Message Importance="high" Condition="'%(_RaskExternalFile.PackageModule)' != ''"
                              Text="FILE=%(_RaskExternalFile.Filename)%(_RaskExternalFile.Extension)|%(_RaskExternalFile.PackageModule)"/>
                   </Target>
                   <Target Name="ReportAdditionalFiles">
                     <Message Importance="high" Text="ADDITIONAL=@(AdditionalFiles->'%(Filename)%(Extension)', ' ADDITIONAL=')"/>
                   </Target>
                 </Project>
                 """);

            return await Run("dotnet", $"msbuild \"{project}\" {arguments} -nologo", temp);
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }
}
