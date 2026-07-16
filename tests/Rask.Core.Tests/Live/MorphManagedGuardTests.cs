using System.Diagnostics;
using System.Text.Json;

namespace Rask.Core.Tests.Live;

// Regression guard for issue #419: the playground accumulated an empty .pg-code-host per
// full-HTML frame because data-rask-managed sat on a node the .NET side ALSO rendered.
//
// morph filters data-rask-managed nodes out of the existing (from) child list but, before the
// fix, not the incoming (to) list — so a marked node present in the payload had its from copy
// filtered out and its to copy left unpaired, appended fresh every morph (unbounded growth). The
// guard makes the to-side filter symmetric: a marked node in the incoming tree is always a misuse
// (a rendered node is part of the payload), so skipping it turns the mistake into a no-op.
//
// This exercises the production rask-morph.js in a Node subprocess with a stub DOM. The
// user-observable side is covered by PlaygroundExampleTests (.pg-code-host count after a run).
public sealed class MorphManagedGuardTests
{
    [Fact]
    public void ManagedNodeInIncomingTree_IsNotDuplicated_AndCorrectlyPlacedMarkerSurvives()
    {
        var node = ResolveNode();
        if (node is null)
        {
            // No node on PATH — the JS-driven reproduction can't run. Don't hard-fail; the
            // playground E2E covers the user-observable side.
            return;
        }

        var repoRoot = LocateRepoRoot();
        var fixtureScript = Path.Combine(repoRoot, "tests", "Rask.Core.Tests", "Live", "MorphManagedGuardFixture.mjs");
        var morphPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-morph.js");
        Assert.True(File.Exists(fixtureScript), $"Fixture script missing: {fixtureScript}");
        Assert.True(File.Exists(morphPath), $"Morph source missing: {morphPath}");

        var psi = new ProcessStartInfo(node, $"\"{fixtureScript}\" \"{morphPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
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

        using var doc = JsonDocument.Parse(jsonLine!);
        var root = doc.RootElement;

        bool GetBool(string name) => root.GetProperty(name).GetBoolean();
        int GetInt(string name) => root.GetProperty(name).GetInt32();

        // The misuse (marker on the rendered host) is a no-op: after two morph frames the host is
        // still single, not duplicated, and the original Monaco DOM is untouched.
        Assert.Equal(1, GetInt("misuseHostCount"));
        Assert.True(GetBool("misuseMonacoKept"), "the original host / Monaco DOM was disturbed by the guard");

        // The correct placement (marker on Monaco's own child) survives a childless incoming host.
        Assert.Equal(1, GetInt("correctHostCount"));
        Assert.True(GetBool("correctMonacoKept"), "a correctly-marked library child was stripped by morph");
    }

    private static string? ResolveNode()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var exeNames = OperatingSystem.IsWindows() ? new[] { "node.exe", "node.cmd" } : new[] { "node" };
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
