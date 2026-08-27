using System.Diagnostics;

namespace Rask.Spa.Hosting.Tests;

/// <summary>
///     The Node floor is enforced, not merely documented.
/// </summary>
/// <remarks>
///     <para>
///         <c>RaskSpaMinimumNode</c> was documented as a floor long before anything compared against it.
///         It was interpolated into the RASKSPA001 "node did not run" message and read nowhere else, and
///         <c>_RaskSpaNodeVersion</c> was captured out of the probe and dropped on the floor. A too-old
///         Node therefore sailed past the probe, reached <c>vite</c>, and failed with the <c>engines</c>
///         error the probe exists to prevent — and setting <c>RaskSpaMinimumNode</c> insisted on nothing.
///     </para>
///     <para>
///         So these drive the SHIPPED <c>build/Rask.Spa.Hosting.props</c> and <c>.targets</c> through a
///         real MSBuild, rather than asserting over a copy of their text. A substring test over the
///         targets file cannot tell an enforced floor from an interpolated one: both mention the property.
///     </para>
///     <para>
///         Both directions are exercised on purpose. A gate that always fails is not a gate, so the
///         satisfied-floor case is the negative control.
///     </para>
/// </remarks>
public sealed class NodeFloorGateTests
{
    /// <summary>A floor no release can satisfy: the probe must refuse, naming what it found.</summary>
    [SkippableFact]
    public async Task A_node_below_the_floor_is_refused()
    {
        var node = await NodeVersion();
        Skip.If(node is null, "node is not on PATH, so the floor gate was never exercised.");

        var (exit, output) = await Probe("99.0.0");

        Assert.True(exit != 0, $"a Node below the floor built anyway.\n{output}");
        Assert.Contains("RASKSPA005", output, StringComparison.Ordinal);

        // The message has to name the version it found, or the reader cannot tell what to install.
        Assert.Contains(node!, output, StringComparison.Ordinal);
        Assert.Contains("99.0.0", output, StringComparison.Ordinal);
    }

    /// <summary>The negative control: a satisfied floor is silent.</summary>
    [SkippableFact]
    public async Task A_node_above_the_floor_is_accepted()
    {
        Skip.If(await NodeVersion() is null, "node is not on PATH, so the floor gate was never exercised.");

        var (exit, output) = await Probe("0.0.1");

        Assert.True(exit == 0, $"a Node above the floor was refused.\n{output}");
        Assert.DoesNotContain("RASKSPA005", output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The default floor ships satisfied by the current LTS — the version this machine runs is on or
    ///     above it, so <c>rask new</c> does not scaffold a project the developer's own Node cannot build.
    /// </summary>
    [SkippableFact]
    public async Task The_shipped_default_floor_accepts_the_current_lts()
    {
        var node = await NodeVersion();
        Skip.If(node is null, "node is not on PATH.");
        Skip.If(
            Version.Parse(node!).Major < 22,
            $"this machine runs Node {node}, below every supported line; the default floor cannot be judged here.");

        // No RaskSpaMinimumNode override: whatever build/Rask.Spa.Hosting.props ships is what runs.
        var (exit, output) = await Probe(minimum: null);

        Assert.True(exit == 0, $"the shipped default floor refuses Node {node}.\n{output}");
    }

    /// <summary>
    ///     Runs the real <c>_RaskSpaProbeNode</c> target out of the package's own build assets, against a
    ///     throwaway project whose only job is to resolve a client directory so the probe is reachable.
    /// </summary>
    private static async Task<(int Exit, string Output)> Probe(string? minimum)
    {
        var build = Path.Combine(RepoRoot(), "src", "Rask.Spa.Hosting", "build");
        var temp = Path.Combine(Path.GetTempPath(), "rask-spa-floor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "Client"));
        try
        {
            var project = Path.Combine(temp, "probe.proj");
            await File.WriteAllTextAsync(
                project,
                $"""
                 <Project>
                   <Import Project="{Path.Combine(build, "Rask.Spa.Hosting.props")}"/>
                   <PropertyGroup><RaskSpaClientDir>Client</RaskSpaClientDir></PropertyGroup>
                   <Import Project="{Path.Combine(build, "Rask.Spa.Hosting.targets")}"/>
                 </Project>
                 """);

            var floor = minimum is null ? string.Empty : $" -p:RaskSpaMinimumNode={minimum}";
            return await Run("dotnet", $"msbuild \"{project}\" -t:_RaskSpaProbeNode -nologo{floor}");
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

    /// <summary>The version this machine reports, normalised the way the targets normalise it.</summary>
    private static async Task<string?> NodeVersion()
    {
        try
        {
            var (exit, output) = await Run("node", "--version");
            return exit == 0 ? output.Trim().TrimStart('v') : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static async Task<(int Exit, string Output)> Run(string file, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
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
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("Could not locate the repo root (Rask.slnx) from the test base directory.");
    }
}
