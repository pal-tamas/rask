using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Rask.TestSupport;

/// <summary>
///     Runs a <c>.mjs</c> fixture in a node subprocess and returns the JSON line it printed.
/// </summary>
/// <remarks>
///     For the client-side code Rask actually ships — the morph, the island runtime. These fixtures
///     exercise the production <c>.js</c> against a stub DOM rather than a C# port of it, because a
///     port would only ever pin the port.
/// </remarks>
public static class NodeFixture
{
    /// <summary>
    ///     Runs <paramref name="fixture" />, passing each of <paramref name="arguments" /> as an
    ///     absolute path.
    /// </summary>
    /// <param name="fixture">The fixture script, as a repo-relative path.</param>
    /// <param name="arguments">Repo-relative paths handed to the fixture, in order.</param>
    /// <returns>The parsed JSON line, or null when there is no node on PATH.</returns>
    /// <remarks>
    ///     A missing node is not a failure: these fixtures are a second line of defence behind the
    ///     browser E2E, and none of the .NET projects need a JavaScript toolchain to build or test. But
    ///     the skip is announced rather than silent — xUnit 2.x has no runtime skip, so a bare return
    ///     reports as a PASS, and a gate that quietly stops running is worse than one that fails.
    /// </remarks>
    public static JsonDocument? Run(string fixture, params string[] arguments)
    {
        var node = ResolveNode();
        if (node is null)
        {
            Console.WriteLine(
                $"NodeFixture: no 'node' on PATH — {fixture} did NOT run. "
                + "The browser E2E covers the user-observable side.");
            return null;
        }

        var repoRoot = LocateRepoRoot();

        var fixturePath = Path.Combine(repoRoot, fixture.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fixturePath), $"Fixture script missing: {fixturePath}");

        var argv = new List<string> { Quote(fixturePath) };
        foreach (var argument in arguments)
        {
            var path = Path.Combine(repoRoot, argument.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Fixture argument missing: {path}");
            argv.Add(Quote(path));
        }

        var psi = new ProcessStartInfo(node, string.Join(" ", argv))
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

    private static string Quote(string path) => $"\"{path}\"";

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
