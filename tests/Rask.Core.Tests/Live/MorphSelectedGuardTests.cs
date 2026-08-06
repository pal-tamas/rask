using System.Diagnostics;
using System.Text.Json;

namespace Rask.Core.Tests.Live;

// Regression guard for the <select> desync (#588) — the third form property, and the only one that had
// no lagging-frame guard.
//
// Symptom: the user picks an option (the browser flips that option's `selected` PROPERTY and leaves
// every `selected` ATTRIBUTE where the server put it), then a re-render the server computed BEFORE the
// pick reached it lands. The diff codec's syncFormProperty set `.selected` unconditionally, so the box
// snapped back to the old option until the echo arrived. The focus guard in morph() doesn't help, for
// the same reason it doesn't help a date input: a select commits on change, so focus has moved on by
// the time the lagging frame lands (see MorphValueGuardTests).
//
// Fix: raskNotePendingSelected records the pre-pick `selected` attribute of EVERY option on dispatch —
// the whole select, exactly as the checked guard records the whole radio group, because a stale frame
// re-selecting the previously chosen option natively deselects the new one. raskShouldSuppressSelected
// then suppresses any frame still carrying that state until an authoritative one differs and releases.
//
// Second half: applying a selection through the SELECT's index rather than the option's own property,
// so one write moves the whole group instead of leaving a single-select momentarily showing its first
// option between a remove-op and a set-op.
//
// Exercises the production rask-morph.js + rask-dom.js in a Node subprocess with a stub DOM, alongside
// MorphCheckedGuardTests. Pairs with the WASM/Server E2E journeys, which cover the user-visible side.
public sealed class MorphSelectedGuardTests
{
    [Fact]
    public void Selected_StaleRender_DoesNotClobberTheJustPickedOption_ThenReleases()
    {
        var node = ResolveNode();
        if (node is null)
        {
            // No node on PATH — the JS-driven reproduction can't run. Don't hard-fail; the E2E
            // journeys cover the user-observable side.
            return;
        }

        var repoRoot = LocateRepoRoot();
        var fixtureScript = Path.Combine(repoRoot, "tests", "Rask.Core.Tests", "Live", "MorphSelectedGuardFixture.mjs");
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

        // THE regression assertion. A lagging frame must neither re-select the option the server still
        // thinks is chosen (which natively deselects the new one) nor clear the one the user picked.
        Assert.False(Get("s1AfterStaleA"), "stale frame re-selected the old option");
        Assert.True(Get("s1AfterStaleB"), "stale frame reverted the user's pick");

        // The authoritative echo applies and releases both guards.
        Assert.False(Get("s1AfterEchoA"), "echo didn't clear the old option");
        Assert.True(Get("s1AfterEchoB"), "echo didn't apply to the picked option");

        // Released — a later server-driven change is not pinned by the guard.
        Assert.True(Get("s1AfterLaterA"), "guard pinned the selection after the echo");
        Assert.False(Get("s1AfterLaterB"), "moving the selection left the old option selected");

        // A select nobody has touched has no guard at all, so ordinary server-driven selection is
        // untouched by any of this.
        Assert.False(Get("s2AfterServerA"), "server-driven deselect didn't apply");
        Assert.True(Get("s2AfterServerB"), "server-driven select didn't apply");

        // Selecting moves the whole group in one write, rather than depending on a sibling's op also
        // arriving (and surviving the guard) to clear the option that was on.
        Assert.True(Get("s3OnlyCSelected"), "selecting one option didn't clear its siblings");

        // ...except on a multi-select, where several options are legitimately on at once.
        Assert.True(Get("s4BothSelected"), "a multi-select lost an already-selected option");
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
