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
    /// <remarks>
    ///     A silent pass is also what a SKIPPED target looks like, so this asserts the probe actually ran
    ///     rather than only that nothing complained. If the target's Condition ever stops holding for the
    ///     throwaway project — the package.json gate moves, the <c>**/*.tsx</c> glob gains an Exclude —
    ///     MSBuild exits 0 having done nothing and both of the other assertions here would still pass.
    /// </remarks>
    [SkippableFact]
    public async Task A_node_above_the_floor_is_accepted()
    {
        Skip.If(await NodeVersion() is null, "node is not on PATH, so the floor gate was never exercised.");

        var (exit, output) = await Probe("0.0.1", verbose: true);

        Assert.True(exit == 0, $"a Node above the islands floor was refused.\n{output}");
        Assert.DoesNotContain("RASKISLAND001", output, StringComparison.Ordinal);

        Assert.Contains(
            "_RaskExternalProbeNode",
            output,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     The shipped default accepts the Node this machine runs, so a developer who can build the repo
    ///     can build an island in it.
    /// </summary>
    /// <remarks>
    ///     The skip is measured against the SHIPPED floor, read from the props, not against a major
    ///     written here. A literal `Major &lt; 22` would let a machine on 22.0–22.11 through the guard and
    ///     then fail the assertion — reporting "the shipped floor refuses Node 22.5.0" as a defect when
    ///     the floor is doing exactly its job. A test whose whole subject is "a copy of a number is a
    ///     third place for it to be wrong" must not keep a copy of the number.
    ///     <para>
    ///         An unparseable version (a nightly, an RC) skips rather than throwing, because the probe
    ///         itself deliberately lets those through — the test has to tolerate what production tolerates.
    ///     </para>
    /// </remarks>
    [SkippableFact]
    public async Task The_shipped_default_floor_accepts_the_current_lts()
    {
        var node = await NodeVersion();
        Skip.If(node is null, "node is not on PATH.");

        var floor = ShippedFloor();
        Skip.If(
            !Version.TryParse(node, out var running),
            $"node reports '{node}', which the probe itself skips rather than comparing.");
        Skip.If(
            running < floor,
            $"this machine runs Node {node}, below the shipped floor ({floor}); it cannot judge the default.");

        var (exit, output) = await Probe(minimum: null);

        Assert.True(exit == 0, $"the shipped islands floor ({floor}) refuses Node {node}.\n{output}");
    }

    /// <summary>The floor <c>Rask.External.props</c> actually ships, read rather than restated.</summary>
    private static Version ShippedFloor()
    {
        var props = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Rask.External", "build", "Rask.External.props"));

        var declared = System.Text.RegularExpressions.Regex.Match(
            props, @"<RaskExternalMinimumNode[^>]*>([0-9.]+)</RaskExternalMinimumNode>");

        Assert.True(declared.Success, "RaskExternalMinimumNode is no longer declared in Rask.External.props.");
        return Version.Parse(declared.Groups[1].Value);
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
    private static async Task<(int Exit, string Output)> Probe(string? minimum, bool verbose = false)
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

            // Normal verbosity does not name the targets it ran, so the reachability assertion needs -v:n.
            var verbosity = verbose ? " -v:n" : string.Empty;

            return await Run(
                "dotnet", $"msbuild \"{project}\" -t:_RaskExternalProbeNode -nologo{floor}{verbosity}", temp);
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

    /// <remarks>
    ///     Both pipes are drained CONCURRENTLY — the reads are started and only awaited after the process
    ///     exits. Awaiting stdout to completion first deadlocks whenever the child fills the stderr pipe
    ///     buffer (~64 KB) while the parent is still blocked on stdout: the child blocks writing, never
    ///     exits, and stdout never closes. `dotnet msbuild` on a cold agent — NuGet output, first-run
    ///     messages, and the failing-build path this class exists to exercise — is exactly the shape that
    ///     produces that much stderr. <c>NodeFloorGateTests.Run</c> does it this way for the same reason.
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
