using System.Diagnostics;

namespace Rask.External.Tests;

/// <summary>
///     A Lit island and an ordinary scoped <c>.ts</c> in one project, with neither opt-out set (#938).
/// </summary>
/// <remarks>
///     <para>
///         Both features are spelled <c>Name.ts</c> beside <c>Name.cs</c>, and until this was fixed a
///         project could only say which ONE of them it had: <c>RaskScopedTsAutoInclude=false</c> for an
///         islands-only project, <c>RaskExternalLitAutoPair=false</c> for a scoped-TypeScript one. With
///         both, the two file lists claimed each other's files in both directions — island discovery
///         handed the bundler a scoped module that never default-exported a tag name, and the scoped
///         glob compiled the island's module as a component asset.
///     </para>
///     <para>
///         Driven through real MSBuild against the shipped build assets, for the reason
///         <see cref="IslandNodeFloorGateTests" /> gives: the subject IS the interaction between two
///         packages' targets — an item defined in <c>Rask.Core.targets</c>, narrowed by a target in
///         <c>Rask.External.targets</c>, at a point in the order neither file states on its own. A C#
///         test over the task alone would prove the task and nothing about the wiring, and the wiring
///         is where every previous version of this bug lived.
///     </para>
///     <para>
///         No <c>package.json</c> and no node: the separation happens during discovery, long before
///         anything is bundled, and a test that needed a bundler could not run on a machine without one.
///     </para>
/// </remarks>
public sealed class IslandScopedTsSeparationTests
{
    /// <summary>Island discovery does not claim the scoped file.</summary>
    [Fact]
    public async Task A_scoped_TypeScript_file_is_not_offered_to_the_bundler()
    {
        var report = await Discover();

        // The positive half first: a discovery that found NOTHING would satisfy the assertion below
        // while having proved only that the target was skipped.
        Assert.Contains("ISLAND=Gauge.ts", report, StringComparison.Ordinal);

        Assert.DoesNotContain("ISLAND=Panel.ts", report, StringComparison.Ordinal);
    }

    /// <summary>The scoped-TypeScript list does not claim the island's module.</summary>
    [Fact]
    public async Task An_island_module_is_not_compiled_as_a_scoped_asset()
    {
        var report = await Discover();

        Assert.Contains("SCOPED=Panel.ts", report, StringComparison.Ordinal);

        Assert.DoesNotContain("SCOPED=Gauge.ts", report, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds a throwaway project holding one of each, and reports both lists.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>@(_RaskScopedTs)</c> is reported by IDENTITY rather than by full path on purpose: its
    ///         glob is project-relative, and the removal has to match items in exactly the shape that
    ///         glob produced. Rebuilding the list instead of removing from it would also drop
    ///         <c>%(RecursiveDir)</c>, which only the <c>**</c> in that Include can populate and which
    ///         both the compile's Outputs and the AdditionalFiles transform resolve through.
    ///     </para>
    ///     <para>
    ///         The project is a bare <c>&lt;Project&gt;</c> rather than an SDK one, so it needs no
    ///         restore and reaches no network — and it therefore has to stub
    ///         <c>ResolveProjectReferences</c>, which the pairing target depends on because the task
    ///         assembly is produced by exactly that reference edge in this repository. A stub is
    ///         honest here: there is no reference to resolve, and the DLL is already on disk.
    ///     </para>
    /// </remarks>
    private static async Task<string> Discover()
    {
        var build = Path.Combine(RepoRoot(), "src");
        var temp = Path.Combine(Path.GetTempPath(), "rask-island-scoped", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(temp, "Gauge.cs"),
                "public sealed partial class Gauge : Rask.External.LitComponent { }\n");
            await File.WriteAllTextAsync(
                Path.Combine(temp, "Gauge.ts"), "export default 'rask-gauge';\n");

            await File.WriteAllTextAsync(
                Path.Combine(temp, "Panel.cs"),
                "public sealed partial class Panel : Rask.Core.Component { }\n");
            await File.WriteAllTextAsync(
                Path.Combine(temp, "Panel.ts"), "export function mount(): void { }\n");

            var project = Path.Combine(temp, "separation.proj");
            await File.WriteAllTextAsync(
                project,
                $"""
                 <Project>
                   <Import Project="{Path.Combine(build, "Rask.External", "build", "Rask.External.props")}"/>
                   <Import Project="{Path.Combine(build, "Rask.Core", "build", "Rask.Core.targets")}"/>
                   <Import Project="{Path.Combine(build, "Rask.External", "build", "Rask.External.targets")}"/>
                   <ItemGroup>
                     <Compile Include="**\*.cs"/>
                   </ItemGroup>
                   <Target Name="ResolveProjectReferences"/>
                   <Target Name="ReportIslands" DependsOnTargets="_RaskExternalExcludeIslandsFromScopedTs">
                     <Message Importance="high" Text="ISLAND=%(_RaskExternalFile.Filename)%(_RaskExternalFile.Extension)"/>
                     <Message Importance="high" Text="SCOPED=%(_RaskScopedTs.Identity)"/>
                   </Target>
                 </Project>
                 """);

            var (exit, output) = await Run(
                "dotnet", $"msbuild \"{project}\" -t:ReportIslands -nologo", temp);

            Assert.True(exit == 0, $"the discovery build failed.\n{output}");
            return output;
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a green test over.
            }
        }
    }

    /// <remarks>
    ///     Both pipes are drained CONCURRENTLY, for the reason <see cref="IslandNodeFloorGateTests" />
    ///     records: awaiting stdout to completion first deadlocks whenever the child fills the stderr
    ///     buffer while the parent is still blocked on stdout.
    /// </remarks>
    private static async Task<(int Exit, string Output)> Run(string file, string arguments, string workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
            },
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdout + await stderr);
    }

    private static string RepoRoot()
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
