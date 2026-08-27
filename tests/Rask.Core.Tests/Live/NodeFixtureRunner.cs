using System.Diagnostics;
using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     Runs a <c>.mjs</c> fixture in a node subprocess and hands back the JSON line it printed.
/// </summary>
/// <remarks>
///     Shared by the morph fixtures, which exercise the production
///     <c>Rask.Core/Resources/rask-morph.js</c> rather than a C# re-implementation of it — the client
///     morph is real shipped code with real bugs, and a port would only pin the port.
/// </remarks>
internal static class NodeFixtureRunner
{
    /// <summary>
    ///     Runs <paramref name="fixtureFileName" /> from <c>tests/Rask.Core.Tests/Live</c>, or returns
    ///     null when there is no node on PATH.
    /// </summary>
    /// <remarks>
    ///     A missing node is not a failure: these fixtures are a second line of defence behind the
    ///     browser E2E, and Rask.Core itself needs no JavaScript toolchain to build or test. But the
    ///     skip is announced rather than silent — xUnit 2.x has no runtime skip, so a bare `return`
    ///     reports as a PASS, and a gate that quietly stops running is worse than one that fails.
    /// </remarks>
    public static JsonDocument? Run(string fixtureFileName)
    {
        var node = ResolveNode();
        if (node is null)
        {
            Console.WriteLine(
                $"NodeFixtureRunner: no 'node' on PATH — {fixtureFileName} did NOT run. "
                + "The browser E2E covers the user-observable side.");
            return null;
        }

        var repoRoot = LocateRepoRoot();
        var fixture = Path.Combine(repoRoot, "tests", "Rask.Core.Tests", "Live", fixtureFileName);
        var morph = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-morph.js");
        Assert.True(File.Exists(fixture), $"Fixture script missing: {fixture}");
        Assert.True(File.Exists(morph), $"Morph source missing: {morph}");

        var psi = new ProcessStartInfo(node, $"\"{fixture}\" \"{morph}\"")
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

        Assert.True(proc.ExitCode == 0,
            $"Fixture exited with code {proc.ExitCode}. stderr:\n{stderr}\nstdout:\n{stdout}");

        var jsonLine = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(s => s.StartsWith('{') && s.EndsWith('}'));
        Assert.False(jsonLine is null,
            $"Fixture didn't emit a JSON line. stdout:\n{stdout}\nstderr:\n{stderr}");

        return JsonDocument.Parse(jsonLine!);
    }

    private static string? ResolveNode()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var exeNames = OperatingSystem.IsWindows() ? ["node.exe", "node.cmd"] : new[] { "node" };
        foreach (var dir in path.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in exeNames)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}
