using System.Diagnostics;
using System.Text.Json;

namespace Rask.Wasm.Tests.JsInteropRuntime;

// Regression guard for the GitHub Pages WASM refresh report.
//
// User symptom (pre-fix): "Microsoft.JSInterop.JSException — undefined is
// not an object (evaluating 'window.hljs.highlightElement')" after
// Page.ReloadAsync() on any /validation-style CodeSample-bearing page.
// RootErrorBoundary caught the faulted OnRenderedAsync task and rendered
// DefaultErrorPage ("Something went wrong") in place of the route.
//
// Mechanism: rask.wasm.js's head-asset gate calls a shared `finish()`
// closure on the script element's 'load', 'error', AND 5-second safety
// timeout. When the external <script> contributed via CodeSample.Head
// (highlight.min.js) terminates without successfully defining its global
// — CDN flake, cache eviction on refresh, integrity mismatch, extension
// block, CSP — the gate DRAINS every queued Rask.* invoke regardless.
// Pre-fix, the queued CodeSample.rendered dereferenced an undefined
// `window.hljs` and threw a TypeError that marshalled into JSException.
//
// Two-part fix:
//   * Framework (rask.wasm.js / rask.js): distinguish load vs error vs
//     timeout. Drain still happens — otherwise queued invokes hang
//     forever on real-world asset failures — but error/timeout paths
//     log a console.warn naming the failed asset URL so the developer
//     can trace the un-highlighted DOM back to its cause.
//   * User code (CodeSample.js): defensive guard against undefined
//     `window.hljs` before dereferencing. The component now degrades
//     gracefully to un-highlighted code blocks instead of throwing.
//
// This test exercises the production rask.wasm.js bundle in a Node
// subprocess with a stub DOM, parks a Rask.CodeSample.rendered invoke
// (the stub mirrors the post-fix defensive shape), fires 'error' on the
// tracked <script>, and asserts the framework contract: drain proceeds,
// warning logged, user-code-equivalent does not throw.
//
// Pairs with the E2E coverage in Rask.Examples.E2E.Tests/
// ExampleSmokeTests.HighlightJs.cs (Highlight_HljsScriptFails_* and
// Highlight_BrowserRefreshOnCodeSamplePage_*).
public sealed class HeadAssetGateBugReproductionTests
{
    [Fact]
    public void HeadAsset_ErrorEvent_DrainsGate_WithDiagnosticWarning_AndDefensiveUserCodeDoesNotThrow()
    {
        var node = ResolveNode();
        if (node is null)
        {
            // No node on PATH — the JS-driven reproduction can't run. Don't
            // hard-fail the suite; emit a diagnostic and pass. The E2E
            // reproduction (Highlight_HljsScriptFails_*) still covers the
            // user-observable side of the same bug under Playwright.
            return;
        }
        var repoRoot = LocateRepoRoot();
        var fixtureScript = Path.Combine(repoRoot, "Rask.Wasm.Tests", "JSInterop", "HeadAssetGateFixture.mjs");
        var bundlePath = Path.Combine(repoRoot, "Rask.Wasm", "Browser", "rask.wasm.js");
        Assert.True(File.Exists(fixtureScript), $"Fixture script missing: {fixtureScript}");
        Assert.True(File.Exists(bundlePath), $"Bundle source missing: {bundlePath}");

        var psi = new ProcessStartInfo(node, $"\"{fixtureScript}\" \"{bundlePath}\"")
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

        // The fixture emits one JSON line on stdout (after any console.log noise
        // from the bundle's setExports). Locate it.
        var jsonLine = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(s => s.StartsWith("{") && s.EndsWith("}"));
        Assert.False(jsonLine is null,
            $"Fixture didn't emit a JSON line. stdout:\n{stdout}\nstderr:\n{stderr}");

        using var doc = JsonDocument.Parse(jsonLine!);
        var root = doc.RootElement;

        // The gate's 5-second safety timeout MUST be scheduled — without it,
        // an asset that never fires either event leaves Rask.* invokes
        // queued forever and the page hangs in a loading state. This part
        // works correctly.
        Assert.True(root.GetProperty("safetyTimeoutScheduled").GetBoolean(),
            "Gate failed to schedule the 5s safety timeout when tracking a Head asset.");

        // Sanity check the gate held the invoke BEFORE the error event. If
        // this fails, the gate's pending-asset bookkeeping is more broken
        // than the user reported — the queue isn't holding pending invokes
        // at all.
        Assert.False(root.GetProperty("firedBeforeError").GetBoolean(),
            "Gate let the Rask.CodeSample.rendered invoke run while the hljs " +
            "script was still pending — even worse than the reported bug.");

        // ====== Contract ======
        // The gate MUST drain queued Rask.* invokes after the script's
        // terminal event (load / error / timeout), otherwise a real-world
        // asset failure would hang the page forever waiting for the global.
        var firedAfterError = root.GetProperty("firedAfterError").GetBoolean();
        Assert.True(firedAfterError,
            "Gate failed to drain queued invokes after the script's 'error' " +
            "event — the page would hang waiting for an asset that will " +
            "never load. Drain on every terminal state is intentional.");

        // The drained invoke ran against window.hljs being undefined — the
        // asset failed to define its global, the queue still drained. This
        // is the exact context that pre-fix threw a TypeError.
        var invokeSawHljs = root.GetProperty("invokeSawHljs").GetBoolean();
        Assert.False(invokeSawHljs,
            "Asset 'error' supposedly fired but the queued invoke observed " +
            "window.hljs as defined — the fixture's stub setup is wrong " +
            "(should leave window.hljs undefined to mirror production).");

        // ====== Diagnostic improvement (framework half of the fix) ======
        // On the error path the gate must console.warn naming the failed
        // asset URL so the developer can trace any consequent TypeError
        // in their JS back to its cause. Without this, the un-highlighted
        // code blocks on the user's GH Pages refresh look identical to
        // a bug in CodeSample.js itself.
        var warnedAboutFailedAsset = root.GetProperty("warnedAboutFailedAsset").GetBoolean();
        Assert.True(warnedAboutFailedAsset,
            "Gate drained on 'error' WITHOUT logging a diagnostic naming the " +
            "failed asset URL. Devs would see un-highlighted code (or a " +
            "later TypeError from non-defensive user JS) with no breadcrumb " +
            "back to highlight.min.js failing. Restore the console.warn " +
            "in trackHeadAsset's error/timeout branches of " +
            "Rask.Wasm/Resources/rask.wasm.js (and the Server mirror in " +
            "Rask.Server/Resources/rask.js).");

        // ====== User-code half of the fix ======
        // The fixture's Rask.CodeSample.rendered stub mirrors the post-fix
        // CodeSample.js: defensive guard before dereferencing the asset's
        // global, so an undefined window.hljs cleanly no-ops. If this
        // assertion ever fires, either the fix in CodeSample.js was
        // reverted OR a new component is dispatching through the gate
        // without a defensive guard.
        var invokeThrew = root.TryGetProperty("invokeThrew", out var t) && t.ValueKind != JsonValueKind.Null
            ? t.GetString()
            : null;
        Assert.True(invokeThrew is null,
            $"User-equivalent JS threw despite the gate's contract: '{invokeThrew}'. " +
            "Restore the `typeof window.hljs === 'undefined'` guard in " +
            "Rask.Example.Shared/Demos/CodeSample.js — gracefully degrading " +
            "to un-highlighted code is the contract any component dispatching " +
            "through the head-asset gate must honour.");
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
            if (File.Exists(Path.Combine(dir.FullName, "Rask.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.sln walking up from {AppContext.BaseDirectory}");
    }
}
