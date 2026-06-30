using System.Diagnostics;
using System.Text.Json;

namespace Rask.Core.Tests.Live;

// Regression guard for the keyed <head> reconciliation crash on WASM static-host
// hydration (E2E: StandaloneWasmExampleTests.Journey_WalksEveryPageAndUnusualActivity).
//
// Symptom: a WASM app served by a plain static host rendered a blank page; the .NET
// runtime booted and applied its first render, then the second morph threw
// "insertBefore ... reference node is not a child" in _raskMoveBefore.
//
// Cause: the App's <head> carries a keyed scoped-bundle <link> (data-rask-key="rsk-css"),
// which promotes the whole <head> to keyed reconciliation. It hydrates against the SDK
// index.html <head> (<base> + importmap <script> + <title>, none keyed); those from-side
// nodes don't match the new tree by node name and get removed. The keyed loop's `anchor`
// still pointed at a removed node, so the next insert referenced a node no longer in the
// parent. On the Server the <head> is fully Rask-rendered (no SDK nodes), so it never hit
// this.
//
// Fix: advance `anchor` past a from-node before removing it (the node-name-mismatch branch).
//
// This exercises the production rask-morph.js in a Node subprocess with a stub DOM whose
// insertBefore throws exactly like a browser. Pairs with the StandaloneWasm E2E.
public sealed class KeyedHeadMorphTests
{
    [Fact]
    public void KeyedHeadMorph_AgainstSdkInjectedHead_DoesNotThrow_AndConverges()
    {
        var node = ResolveNode();
        if (node is null)
        {
            // No node on PATH — the JS-driven reproduction can't run. Don't hard-fail;
            // the StandaloneWasm E2E covers the user-observable side.
            return;
        }

        var repoRoot = LocateRepoRoot();
        var fixtureScript = Path.Combine(repoRoot, "tests", "Rask.Core.Tests", "Live", "KeyedHeadMorphFixture.mjs");
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

        // The keyed reconciliation must NOT throw — this is the assertion that fails pre-fix
        // (insertBefore against the stale anchor pointing at the removed <base>).
        Assert.False(root.GetProperty("threw").GetBoolean(),
            $"Keyed head morph threw: {root.GetProperty("error").GetString()}");

        // And it must converge the from-head to the App's head: <title> then the keyed <link>,
        // with the SDK-injected <base>/<script> removed.
        var children = root.GetProperty("children").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "TITLE", "LINK" }, children);
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
