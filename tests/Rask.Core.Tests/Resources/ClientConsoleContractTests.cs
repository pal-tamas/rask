namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract that no shipped client traces to <c>console.log</c>. Structural assertions
///     over the <c>.js</c> for the same reason as <see cref="FormGuardClientContractTests" />: these files
///     boot against a live document and still carry unsubstituted <c>@@RASK_*@@</c> splice markers, so
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
        { "Rask.Server", ["src", "Rask.Server", "Resources", "rask.js"] },
        { "Rask.Wasm (source)", ["src", "Rask.Wasm", "Resources", "rask.wasm.js"] },
        // The committed build artifact. It is spliced from the source above by _RaskSpliceClientJs and
        // checked in, so it can drift silently — and it is the copy the browser actually downloads.
        { "Rask.Wasm (committed artifact)", ["src", "Rask.Wasm", "Browser", "rask.wasm.js"] },
        { "shared: rask-morph.js", ["src", "Rask.Core", "Resources", "rask-morph.js"] },
        { "shared: rask-dom.js", ["src", "Rask.Core", "Resources", "rask-dom.js"] },
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
