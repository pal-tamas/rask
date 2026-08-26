using System.Diagnostics;
using System.Text.Json;

namespace Rask.Wasm.Tests.JsInteropRuntime;

/// <summary>
///     What a browser-WASM app does when it fails to start (#817).
/// </summary>
/// <remarks>
///     <para>
///         Before this, <c>main.js</c> was four bare top-level <c>await</c>s with no <c>try</c> anywhere and
///         no global rejection handler. Every way of failing to mount — a 404 on <c>_framework</c>, a wrong
///         content type, an import-map/SRI drift, an empty scoped-asset bake — produced the same thing: the
///         splash spinner, turning, for ever. No console error, no page error. The first symptom was a
///         Playwright locator timing out after 90-120 seconds against a boot screen, which names nothing and
///         reads as a hang, and that is why every occurrence cost hours.
///     </para>
///     <para>
///         Driven through a Node subprocess against the shipped <c>Browser/main.js</c> rather than asserted
///         over the file's text, because the thing worth guarding is behaviour under a real rejection —
///         which step reports, that it reports once, and above all that it stays silent once the app has
///         painted. A text assertion would pass on code that never runs.
///     </para>
/// </remarks>
public sealed class BootFailureReportingTests
{
    /// <summary>The commonest real failure, and the one the issue was opened for.</summary>
    [SkippableFact]
    public void A_runtime_that_will_not_load_says_so_on_the_page_instead_of_spinning()
    {
        var result = RunFixture("runtime-fails");

        Assert.True(result.GetProperty("bootErrorAttributeSet").GetBoolean(),
            "A failed boot left no [data-rask-boot-error] on the page. That attribute is what lets an E2E "
            + "fixture fail in seconds with a reason instead of timing out on a selector that will never "
            + "appear, and what tells a visitor the app is broken rather than slow.");

        // Which step failed is most of the diagnosis, so the summary has to name it — "something went
        // wrong" would leave the reader exactly where the blank spinner did.
        Assert.Contains("runtime could not be loaded", result.GetProperty("summary").GetString(),
            StringComparison.Ordinal);
        Assert.Contains("_framework", result.GetProperty("summary").GetString(), StringComparison.Ordinal);

        // The stack, verbatim. It is the difference between "boot failed" and a fixable report.
        Assert.Contains("Failed to fetch dotnet.native.wasm", result.GetProperty("detail").GetString(),
            StringComparison.Ordinal);

        // Reported once. A boot failure cascades — the throw, then the rejection the rethrow produces,
        // then the never-painted check — and three stacked panels would bury the cause under its echoes.
        Assert.Single(result.GetProperty("consoleErrors").EnumerateArray());

        // Rethrown after reporting: swallowing it would hide the failure from the runtime's own channels
        // and from anything else watching, which trades one silent failure for another.
        Assert.True(result.GetProperty("threw").GetBoolean(),
            "main.js reported the failure but did not rethrow it.");
    }

    /// <summary>
    ///     The silent shape: nothing throws, and nothing ever renders. Deterministic rather than a timeout —
    ///     <c>runMain</c> resolves only after <c>await host.RunAsync&lt;App&gt;()</c> returns, and the first
    ///     frame is pushed from inside it, so a boot screen still on the page at that point is a fact, not a
    ///     guess about how slow the network is.
    /// </summary>
    [SkippableFact]
    public void An_app_that_starts_but_never_renders_is_reported_rather_than_left_spinning()
    {
        var result = RunFixture("never-painted");

        Assert.True(result.GetProperty("bootErrorAttributeSet").GetBoolean(),
            "The app finished starting without ever painting and the page still showed the splash screen.");
        Assert.Contains("never rendered", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The negative control, and the one that matters most: once the app has painted, the boot surface
    ///     must never speak again. A late error belongs to the running app — <c>RootErrorBoundary</c> owns
    ///     that — and painting a full-screen failure panel over a working page would turn a recoverable
    ///     error into a dead app.
    /// </summary>
    [SkippableFact]
    public void Once_the_app_has_painted_the_boot_surface_stays_silent()
    {
        var result = RunFixture("already-painted");

        Assert.False(result.GetProperty("bootErrorAttributeSet").GetBoolean(),
            "The boot surface painted an error over an app that had already mounted.");
        Assert.Empty(result.GetProperty("consoleErrors").EnumerateArray());
        Assert.False(result.GetProperty("styleInjected").GetBoolean(),
            "The boot-error stylesheet was injected into a page that booted fine.");
    }

    /// <summary>
    ///     The handlers have to be installed before the first <c>await</c>, or a failure inside
    ///     <c>dotnet.create()</c> — the step most likely to fail — would escape the very net meant to catch
    ///     it. Asserted on the success path so it cannot be satisfied by the failure path alone.
    /// </summary>
    [SkippableFact]
    public void The_global_failure_handlers_are_installed_before_the_first_await()
    {
        var result = RunFixture("already-painted");

        Assert.True(result.GetProperty("unhandledRejectionHandlerRegistered").GetBoolean());
        Assert.True(result.GetProperty("errorHandlerRegistered").GetBoolean());
        // rask.wasm.js's bootFailed export and .NET's bootFailed JSImport both route here, so that one
        // implementation renders every boot failure rather than each growing half of one.
        Assert.True(result.GetProperty("bootFailedHookExposed").GetBoolean(),
            "__raskBootFailed is not exposed, so rask.wasm.js and the managed side have nothing to report "
            + "to and their failures go back to being console-only.");
    }

    private static JsonElement RunFixture(string scenario)
    {
        var node = ResolveNode();
        Skip.If(node is null, "node is not on PATH, so the JS-driven boot fixture cannot run.");

        var repoRoot = LocateRepoRoot();
        var fixtureScript = Path.Combine(
            repoRoot, "tests", "Rask.Wasm.Tests", "JSInterop", "BootFailureFixture.mjs");
        var mainJs = Path.Combine(repoRoot, "src", "Rask.Wasm", "Browser", "main.js");
        Assert.True(File.Exists(fixtureScript), $"Fixture script missing: {fixtureScript}");
        Assert.True(File.Exists(mainJs), $"Boot script missing: {mainJs}");

        var psi = new ProcessStartInfo(node!, $"\"{fixtureScript}\" \"{mainJs}\" {scenario}")
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

        return JsonDocument.Parse(jsonLine!).RootElement.Clone();
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
