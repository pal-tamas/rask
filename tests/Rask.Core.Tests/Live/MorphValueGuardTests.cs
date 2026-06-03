using System.Diagnostics;
using System.Text.Json;

namespace Rask.Core.Tests.Live;

// Regression guard for the date-input value desync (E2E:
// Validation_ValidatableObject_AttributeAndInterfaceErrors_SurfaceTogether).
//
// Symptom: after the user filled #v11-arrival and blurred, the input flipped back
// to the model default. A re-render the server computed BEFORE the change reached
// it landed afterwards; the shared morph (rask-morph.js) treats a change-only
// input's rendered value as canonical and set the stale value back. The focus
// guard didn't help — a change commits on blur, so focus had already moved on.
//
// Fix: raskNotePendingValue records the committed value on dispatch;
// raskShouldSuppressValue suppresses any server value that doesn't match it until
// the server echoes it back (frames arrive in send order, so the stale frame
// precedes the echo), then releases so server-canonical values win again.
//
// This exercises the production rask-morph.js in a Node subprocess with a stub
// DOM. Pairs with the E2E coverage on the Server host.
public sealed class MorphValueGuardTests
{
    [Fact]
    public void Morph_StaleRender_DoesNotClobberCommittedValue_ThenReleasesOnEcho()
    {
        var node = ResolveNode();
        if (node is null)
        {
            // No node on PATH — the JS-driven reproduction can't run. Don't
            // hard-fail; the E2E test covers the user-observable side.
            return;
        }

        var repoRoot = LocateRepoRoot();
        var fixtureScript = Path.Combine(repoRoot, "tests", "Rask.Core.Tests", "Live", "MorphValueGuardFixture.mjs");
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
            .LastOrDefault(s => s.StartsWith("{") && s.EndsWith("}"));
        Assert.False(jsonLine is null,
            $"Fixture didn't emit a JSON line. stdout:\n{stdout}\nstderr:\n{stderr}");

        using var doc = JsonDocument.Parse(jsonLine!);
        var root = doc.RootElement;

        // The stale re-render (carrying the model default) must NOT overwrite the
        // value the user just committed. This is the assertion that fails pre-fix.
        Assert.Equal("2019-12-31", root.GetProperty("afterStale").GetString());

        // The server's echo of the committed value confirms and clears the guard;
        // the value is unchanged.
        Assert.Equal("2019-12-31", root.GetProperty("afterEcho").GetString());

        // A genuine later server-driven change wins — the guard released after the
        // echo, so the framework didn't permanently pin the user's value.
        Assert.Equal("2030-01-01", root.GetProperty("afterLater").GetString());

        // A server CORRECTION (clear a non-nullable int → model snaps to 0) differs
        // from both the user's input and the pre-edit value, so it's authoritative
        // and must apply. Recording the PRE-EDIT value (not the user's) is what keeps
        // this from being suppressed — guards the Binding_NonNullableInt_Clear E2E.
        Assert.Equal("0", root.GetProperty("afterCorrection").GetString());
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
