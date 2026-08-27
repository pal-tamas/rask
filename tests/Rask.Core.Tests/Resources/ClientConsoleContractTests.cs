namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract that no shipped client traces to <c>console.log</c>. Structural assertions
///     over the <c>.js</c> for the same reason as <see cref="FormGuardClientContractTests" />: these files
///     boot against a live document so
///     they cannot be executed in Node.
/// </summary>
/// <remarks>
///     The WASM client logged every event payload — form input included — on a path the comment above it
///     documents as running ~60×/sec while someone types, behind no flag, in production builds. That is a
///     place data lands where nobody expects it, and it also made the console useless for the debugging it
///     was there to serve. <c>console.warn</c> / <c>console.error</c> are deliberately still allowed: they
///     report a fault, they don't narrate the happy path.
/// </remarks>
public class ClientConsoleContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    public static TheoryData<string, string[]> ShippedClients => new()
    {
        { "Rask.Server", ["src", "Rask.Server", "Resources", "rask.ts"] },
        { "Rask.Wasm (source)", ["src", "Rask.Wasm", "Resources", "rask.wasm.ts"] },
        // The BUILT WASM bundle is checked too — it is the copy the browser downloads — but not from
        // here: this project does not reference Rask.Wasm, so on a clean clone the bundle would not
        // exist yet and this would fail on a missing file rather than on a console.log.
        // JsBundleGateTests, in Rask.Wasm.Tests, owns that half.
        { "shared: rask-morph.ts", ["src", "Rask.Core", "Resources", "rask-morph.ts"] },
        { "shared: rask-dom.ts", ["src", "Rask.Core", "Resources", "rask-dom.ts"] },
    };

    [Theory]
    [MemberData(nameof(ShippedClients))]
    public void No_shipped_client_traces_to_the_console(string label, string[] path)
    {
        var js = File.ReadAllText(Path.Combine([_repoRoot, .. path]));

        var lines = js.Split('\n');
        var offenders = lines
            .Select((text, i) => (Line: i + 1, Text: text))
            .Where(l => l.Text.Contains("console.log", StringComparison.Ordinal))
            .Select(l => $"  {label}:{l.Line}: {l.Text.Trim()}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{label} traces to console.log, which ships to production:\n{string.Join("\n", offenders)}\n"
            + "Use console.warn/console.error for a fault, or a breakpoint for a trace. Never log a "
            + "payload: it carries whatever the user typed.");
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
