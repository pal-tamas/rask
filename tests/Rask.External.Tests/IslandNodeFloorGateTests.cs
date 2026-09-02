using System.Diagnostics;

namespace Rask.External.Tests;

/// <summary>
///     The islands build refuses a Node it cannot bundle with, and says so itself.
/// </summary>
/// <remarks>
///     <para>
///         <c>RaskExternalMinimumNode</c> was declared in <c>Rask.External.props</c>, documented as the
///         floor "the probe" enforced, and read by nothing whatsoever — the property appeared exactly once
///         in the repo, in its own declaration. So setting it insisted on nothing, and too old a Node went
///         straight to <c>npm</c> and failed inside vite with the engines error the probe was supposed to
///         replace. <c>Rask.Spa.Hosting</c> had the identical defect and fixed it; this is that fix, on the
///         path that was left behind.
///     </para>
///     <para>
///         Driven through real MSBuild against the package's own shipped build assets, for the reason
///         <c>NodeFloorGateTests</c> gives: a test that reasserts the condition in C# proves the author's
///         belief about the targets, not the targets. Both directions run — a gate that always fails is
///         not a gate.
///     </para>
/// </remarks>
public sealed class IslandNodeFloorGateTests
{
    /// <summary>A floor no release can satisfy: the probe must refuse, naming what it found.</summary>
    [SkippableFact]
    public async Task A_node_below_the_floor_is_refused()
    {
        var node = await NodeVersion();
        Skip.If(node is null, "node is not on PATH, so the floor gate was never exercised.");

        var (exit, output) = await Probe("99.0.0");

        Assert.True(exit != 0, $"a Node below the islands floor built anyway.\n{output}");
        Assert.Contains("RASKISLAND001", output, StringComparison.Ordinal);

        // The message names both numbers, or the reader cannot tell what to install.
        Assert.Contains(node!, output, StringComparison.Ordinal);
        Assert.Contains("99.0.0", output, StringComparison.Ordinal);
    }

    /// <summary>The negative control: a satisfied floor is silent.</summary>
    [SkippableFact]
    public async Task A_node_above_the_floor_is_accepted()
    {
        Skip.If(await NodeVersion() is null, "node is not on PATH, so the floor gate was never exercised.");

        var (exit, output) = await Probe("0.0.1");

        Assert.True(exit == 0, $"a Node above the islands floor was refused.\n{output}");
        Assert.DoesNotContain("RASKISLAND001", output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The shipped default accepts the Node this machine runs, so a developer who can build the repo
    ///     can build an island in it.
    /// </summary>
    [SkippableFact]
    public async Task The_shipped_default_floor_accepts_the_current_lts()
    {
        var node = await NodeVersion();
        Skip.If(node is null, "node is not on PATH.");
        Skip.If(
            Version.Parse(node!).Major < 22,
            $"this machine runs Node {node}, below every supported line; the default cannot be judged here.");

        var (exit, output) = await Probe(minimum: null);

        Assert.True(exit == 0, $"the shipped islands floor refuses Node {node}.\n{output}");
    }

    /// <summary>
    ///     Runs the real <c>_RaskExternalProbeNode</c> target out of the package's own build assets.
    /// </summary>
    /// <remarks>
    ///     The target is conditioned on a project that could actually bundle: a <c>package.json</c> (what
    ///     <c>_RaskExternalBundlable</c> looks for) and at least one island source (the <c>**/*.tsx</c>
    ///     glob). Both are written into the throwaway directory so the probe is reachable at all — without
    ///     them the target is skipped and every assertion above would pass by running nothing.
    /// </remarks>
    private static async Task<(int Exit, string Output)> Probe(string? minimum)
    {
        var build = Path.Combine(RepoRoot(), "src", "Rask.External", "build");
        var temp = Path.Combine(Path.GetTempPath(), "rask-island-floor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temp, "package.json"), """{ "private": true }""");
            await File.WriteAllTextAsync(
                Path.Combine(temp, "Island.tsx"), "export default function Island() { return null; }\n");

            var project = Path.Combine(temp, "probe.proj");
            await File.WriteAllTextAsync(
                project,
                $"""
                 <Project>
                   <Import Project="{Path.Combine(build, "Rask.External.props")}"/>
                   <Import Project="{Path.Combine(build, "Rask.External.targets")}"/>
                 </Project>
                 """);

            var floor = minimum is null ? string.Empty : $" -p:RaskExternalMinimumNode={minimum}";
            return await Run("dotnet", $"msbuild \"{project}\" -t:_RaskExternalProbeNode -nologo{floor}", temp);
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

    private static async Task<string?> NodeVersion()
    {
        try
        {
            var (exit, output) = await Run("node", "--version", Path.GetTempPath());
            return exit == 0 ? output.Trim().TrimStart('v') : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return null;
        }
    }

    private static async Task<(int Exit, string Output)> Run(string file, string arguments, string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        })!;

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout + stderr);
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
