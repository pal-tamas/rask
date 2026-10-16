using static Rask.External.Tests.IslandBuild;

namespace Rask.External.Tests;

/// <summary>
///     Islands are bundled and served by Vite+ alone, and a project that does not install it is told so by name.
/// </summary>
/// <remarks>
///     Driven through real MSBuild against the package's own shipped build assets, for the reason
///     <see cref="IslandNodeFloorGateTests" /> gives: a test that reasserts the condition in C# proves the author's
///     belief about the targets, not the targets. Both directions run — a gate that always fails is not a gate —
///     and the escape hatch too: a project that names its own bundler is not second-guessed.
/// </remarks>
public sealed class IslandVitePlusGateTests
{
    [Fact]
    public async Task A_project_without_vite_plus_is_refused_by_name()
    {
        var (exit, output) = await Check(linkVp: false);

        Assert.True(exit != 0, $"a project that installs no Vite+ passed the islands gate.\n{output}");
        Assert.Contains("RASKISLAND019", output, StringComparison.Ordinal);
        // The message says what to install, or the reader is left to guess.
        Assert.Contains("vite-plus", output, StringComparison.Ordinal);
        Assert.Contains("@voidzero-dev/vite-plus-core", output, StringComparison.Ordinal);
    }

    /// <summary>The negative control, run with -v:d so a SKIPPED target cannot pass for a satisfied one.</summary>
    [Fact]
    public async Task A_project_with_vite_plus_linked_is_accepted()
    {
        var (exit, output) = await Check(linkVp: true, verbose: true);

        Assert.True(exit == 0, $"a project with node_modules/.bin/vp was refused.\n{output}");
        Assert.DoesNotContain("RASKISLAND019", output, StringComparison.Ordinal);
        Assert.Contains("Done building target \"_RaskExternalRequireVitePlus\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bundler_the_project_names_is_not_second_guessed()
    {
        var (exit, output) = await Check(linkVp: false, buildCommand: "my-bundler --config");

        Assert.True(exit == 0, $"a project with its own RaskExternalBuildCommand was refused.\n{output}");
        Assert.DoesNotContain("RASKISLAND019", output, StringComparison.Ordinal);
    }

    /// <summary>Runs the real <c>_RaskExternalRequireVitePlus</c> target out of the package's own build assets.</summary>
    private static async Task<(int Exit, string Output)> Check(bool linkVp, string? buildCommand = null, bool verbose = false)
    {
        var build = Path.Combine(RepoRoot(), "src", "Rask.External", "build");
        var temp = Path.Combine(Path.GetTempPath(), "rask-island-vite-plus", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temp, "package.json"), """{ "private": true }""");
            if (linkVp)
            {
                Directory.CreateDirectory(Path.Combine(temp, "node_modules", ".bin"));
                await File.WriteAllTextAsync(Path.Combine(temp, "node_modules", ".bin", "vp"), "#!/bin/sh\n");
            }

            var project = Path.Combine(temp, "probe.proj");
            await File.WriteAllTextAsync(
                project,
                $"""
                 <Project>
                   <Import Project="{Path.Combine(build, "Rask.External.props")}"/>
                   <Import Project="{Path.Combine(build, "Rask.External.targets")}"/>
                 </Project>
                 """);

            var command = buildCommand is null ? string.Empty : $" \"-p:RaskExternalBuildCommand={buildCommand}\"";
            var verbosity = verbose ? " -v:d" : string.Empty;

            return await Run(
                "dotnet", $"msbuild \"{project}\" -t:_RaskExternalRequireVitePlus -nologo{command}{verbosity}", temp);
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }
}
