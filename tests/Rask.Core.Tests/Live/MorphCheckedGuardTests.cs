using System.Diagnostics;
using System.Text.Json;

namespace Rask.Core.Tests.Live;

// Regression guard for the radio/checkbox `.checked` desync (E2E:
// StandaloneWasmExampleTests.Journey_WalksEveryPageAndUnusualActivity, the Forms
// guide's radio-group step).
//
// Symptom: after the user clicked a radio, a re-render the server computed BEFORE
// the change reached it landed afterwards; both client apply paths (the full morph
// in rask-morph.js and the diff codec's syncFormProperty in rask-dom.js) set
// `.checked` unconditionally, reverting the click. Playwright then reported
// "Clicking the checkbox did not change its state". The `.value` property already
// had a pending-edit guard; `.checked` did not.
//
// Fix: raskNotePendingChecked records the pre-click checked (the `checked`
// attribute a native click leaves untouched) on dispatch — for a radio, the whole
// same-name group; raskShouldSuppressChecked suppresses any frame that still
// carries that stale state until an authoritative frame differs, then releases so
// server-driven changes win again.
//
// This exercises the production rask-morph.js + rask-dom.js in a Node subprocess
// with a stub DOM. Pairs with the WASM/Server E2E journeys.
public sealed class MorphCheckedGuardTests
{
    [Fact]
    public void Checked_StaleRender_DoesNotClobberJustClickedRadioOrCheckbox_ThenReleases()
    {
        var node = ResolveNode();
        if (node is null)
        {
            // No node on PATH — the JS-driven reproduction can't run. Don't
            // hard-fail; the E2E journeys cover the user-observable side.
            return;
        }

        var repoRoot = LocateRepoRoot();
        var fixtureScript = Path.Combine(repoRoot, "tests", "Rask.Core.Tests", "Live", "MorphCheckedGuardFixture.mjs");
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
            .LastOrDefault(s => s.StartsWith("{") && s.EndsWith("}"));
        Assert.False(jsonLine is null,
            $"Fixture didn't emit a JSON line. stdout:\n{stdout}\nstderr:\n{stderr}");

        using var doc = JsonDocument.Parse(jsonLine!);
        var root = doc.RootElement;

        bool Get(string name) => root.GetProperty(name).GetBoolean();

        // Diff codec: the lagging RemoveAttribute-checked op must NOT unset the radio
        // the user just clicked; the SetAttribute echo then applies.
        Assert.True(Get("s1AfterStale"), "stale diff op reverted the clicked radio");
        Assert.True(Get("s1AfterEcho"), "echo diff op didn't apply");

        // Full morph, radio group: a stale frame must neither re-check the previously
        // selected radio (which would natively uncheck the new one) nor unset the new one.
        Assert.False(Get("s2FreeAfterStale"), "stale frame re-checked the old radio");
        Assert.True(Get("s2ProAfterStale"), "stale frame reverted the clicked radio");
        // The echo applies and releases both guards.
        Assert.False(Get("s2FreeAfterEcho"), "echo didn't unset the old radio");
        Assert.True(Get("s2ProAfterEcho"), "echo didn't apply to the clicked radio");
        // The guard released — a later server-driven change is not pinned.
        Assert.False(Get("s2ProAfterLater"), "guard pinned the value after the echo");

        // Full morph, lone checkbox: stale revert suppressed, echo applies.
        Assert.True(Get("s3AfterStale"), "stale frame reverted the checkbox");
        Assert.True(Get("s3AfterEcho"), "checkbox echo didn't apply");
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
