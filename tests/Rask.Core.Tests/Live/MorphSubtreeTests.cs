using System.Diagnostics;
using System.Text.Json;

namespace Rask.Core.Tests.Live;

// Client-behaviour guard for the MorphSubtree diff op (rask-dom.js applyDiff `case 8` →
// rask-morph.js `morph`). MorphSubtree is the Raw-tainted fallback shrunk from a
// full-document morph to one parent's children: the server emits it (FrameDifferTests
// pins the op) and the client morphs just that subtree. This exercises the production
// applyDiff + morph in a Node subprocess and asserts the scoped morph converges without
// touching a focused node outside the morphed parent. Real-browser coverage of the same
// op rides the Playwright E2E guide journeys.
public sealed class MorphSubtreeTests
{
    [Fact]
    public void MorphSubtree_ReconcilesTaintedSubtree_WithoutDisturbingOutsideFocus()
    {
        var node = ResolveNode();
        if (node is null)
        {
            // No node on PATH — the JS-driven reproduction can't run. Don't hard-fail;
            // the guide-page E2E covers the user-observable side.
            return;
        }

        var repoRoot = LocateRepoRoot();
        var dir = Path.Combine(repoRoot, "tests", "Rask.Core.Tests", "Live");
        var fixtureScript = Path.Combine(dir, "MorphSubtreeFixture.mjs");
        var morphPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-morph.js");
        var domPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-dom.js");
        Assert.True(File.Exists(fixtureScript), $"Fixture script missing: {fixtureScript}");
        Assert.True(File.Exists(morphPath), $"Morph source missing: {morphPath}");
        Assert.True(File.Exists(domPath), $"Dom source missing: {domPath}");

        var psi = new ProcessStartInfo(node, $"\"{fixtureScript}\" \"{morphPath}\" \"{domPath}\"")
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

        Assert.False(root.GetProperty("threw").GetBoolean(),
            $"applyDiff threw: {root.GetProperty("error").GetString()}");
        // The op kind must be recognised — a fall-through to the default branch reloads the page.
        Assert.False(root.GetProperty("reloaded").GetBoolean(), "MorphSubtree must not trigger a full reload");

        // The Raw-expanded run reconciled: the <b> was dropped (node-count change) and the sibling
        // <span> text flipped x → y — exactly what a full-document morph would have done, scoped.
        var children = root.GetProperty("children").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "A", "SPAN" }, children);
        Assert.Equal("y", root.GetProperty("spanText").GetString());

        // The focused <input> OUTSIDE the morphed parent kept focus and its place — the morph is
        // scoped to the tainted subtree, never the whole document.
        Assert.True(root.GetProperty("focusKept").GetBoolean(),
            "a focused node outside the morphed parent must keep focus");
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
