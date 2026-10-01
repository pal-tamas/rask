namespace Rask.Wasm.Tests.JsInteropRuntime;

// What the BUILT WASM bundle can be held to. The gate's structure — Rask.* invokes parked until head assets settle
// and window.Rask.{Type} exists — is asserted on the sources in Rask.Core.Tests' HeadAssetGateSourceTests, since a
// Release build minifies every local name away; these ask the shipped bytes only what minification preserves.
public sealed class JsBundleGateTests
{
    [Fact]
    public void The_bundle_does_not_trace_to_the_console()
    {
        // The copy the browser actually downloads. `console.log` in a shipped runtime writes to every
        // visitor's console, and the payloads here carry whatever the user typed — so a trace left in
        // by accident is a privacy problem, not an untidiness.
        //
        // Asserted on the bundle rather than only on the sources it was built from: this project
        // references Rask.Wasm, so the bundle is built before this runs. The source halves live in
        // ClientConsoleContractTests, which cannot make that guarantee.
        var bundle = ReadBrowserBundle();

        var offenders = bundle
            .Split('\n')
            .Select((text, i) => (Line: i + 1, Text: text))
            .Where(l => l.Text.Contains("console.log", StringComparison.Ordinal))
            .Select(l => $"  rask.wasm.js:{l.Line}: {l.Text.Trim()}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "The built WASM bundle traces to console.log, which ships to production:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void The_bundle_ships_the_gate()
    {
        // The one thing worth asking of the built artifact, and the only kind of thing that can be
        // asked of it: minification erases every local name above, but leaves string literals and
        // property names alone. The "Rask." discriminator is the gate's own literal — if the module
        // carrying it were dropped from the bundle (esbuild removes a module whose imports all went
        // unreferenced), this is what would notice.
        var bundle = ReadBrowserBundle();

        Assert.Contains("\"Rask.\"", bundle, StringComparison.Ordinal);
        Assert.Contains("window.__raskPainted", bundle, StringComparison.Ordinal);
    }

    private static string ReadBrowserBundle()
    {
        var repoRoot = LocateRepoRoot();
        var path = Path.Combine(repoRoot, "src", "Rask.Wasm", "Browser", "rask.wasm.js");

        Assert.True(
            File.Exists(path),
            $"'{path}' is missing. It is build output now, not a tracked file — build Rask.Wasm first.");

        return File.ReadAllText(path);
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
