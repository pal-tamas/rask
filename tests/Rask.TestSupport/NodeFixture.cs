using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Rask.TestSupport;

/// <summary>
///     Runs one of a test project's <c>*Fixture.ts</c> harnesses in a Node subprocess and returns the
///     single JSON line it prints.
/// </summary>
/// <remarks>
///     <para>
///         Each fixture drives a real shipped module — the morph, the diff codec, the external-component
///         client runtime — against a stub
///         DOM, in states a browser only reaches through genuine user interaction. The module arrives
///         by <c>import</c>, and MSBuild bundles the fixture into <c>node-fixtures/</c> beside this
///         assembly during the build, so a fixture that no longer compiles fails the build rather
///         than a test.
///     </para>
///     <para>
///         What this replaces: seven copies of the same twenty lines, each reading a framework
///         <c>.js</c> off disk and evaluating it with
///         <c>new Function(src + "return { morph, … }")</c>. That worked while the shared modules
///         were bare declarations meant to be pasted into a host's scope; nothing checked that the
///         names in that string still existed, and it stops working the moment a module has real
///         <c>export</c>s.
///     </para>
/// </remarks>
public static class NodeFixture
{
    /// <summary>
    ///     Runs <paramref name="name" /> and parses its JSON line, or returns <c>null</c> when Node is
    ///     not installed.
    /// </summary>
    /// <remarks>
    ///     A missing Node returns null rather than failing: the browser-observable half of every one
    ///     of these behaviours is covered by an E2E test, and Node is not otherwise required to build
    ///     or test Rask. Callers return early — which is why each one says so at its call site, so a
    ///     silently-skipped test reads as a deliberate choice rather than an accident.
    /// </remarks>
    public static JsonElement? Run(string name)
    {
        var node = ResolveNode();
        if (node is null)
        {
            return null;
        }

        var script = Path.Combine(AppContext.BaseDirectory, "node-fixtures", name + ".mjs");
        Assert.True(
            File.Exists(script),
            $"'{script}' is missing. It is bundled from {name}.ts by the _RaskBundleNodeFixtures target — "
            + "build the test project first.");

        var psi = new ProcessStartInfo(node, $"\"{script}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);

        Assert.True(
            proc.ExitCode == 0,
            $"{name} exited with code {proc.ExitCode}.{Environment.NewLine}"
            + $"stderr:{Environment.NewLine}{stderr}{Environment.NewLine}"
            + $"stdout:{Environment.NewLine}{stdout}");

        var jsonLine = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(s => s.StartsWith('{') && s.EndsWith('}'));

        Assert.False(
            jsonLine is null,
            $"{name} printed no JSON line.{Environment.NewLine}"
            + $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}"
            + $"stderr:{Environment.NewLine}{stderr}");

        // Cloned, so the element outlives the JsonDocument this method disposes.
        using var document = JsonDocument.Parse(jsonLine!);
        return document.RootElement.Clone();
    }

    private static string? ResolveNode()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var exeNames = OperatingSystem.IsWindows() ? new[] { "node.exe", "node.cmd" } : ["node"];

        foreach (var directory in path.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var exe in exeNames)
            {
                var candidate = Path.Combine(directory, exe);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
