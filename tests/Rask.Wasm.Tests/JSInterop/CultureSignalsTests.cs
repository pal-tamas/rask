using System.Text.Json;

namespace Rask.Wasm.Tests.JsInteropRuntime;

/// <summary>
///     <c>getCultureSignals</c> reads the cookie and the query key the app configured, as the server does.
/// </summary>
/// <remarks>Driven under Node against the built <c>rask.wasm.js</c> — see <c>CultureSignalsFixture.ts</c>.</remarks>
public sealed class CultureSignalsTests
{
    [Fact]
    public void A_renamed_cookie_and_query_key_are_the_ones_read()
    {
        if (Run(".AspNetCore.Culture=c%3Dde%7Cuic%3Dde; .My.Culture=c%3Dhu%7Cuic%3Dhu", "?culture=de&lang=hu", ".My.Culture", "lang") is not { } signals)
        {
            return;
        }

        Assert.Equal("c=hu|uic=hu", signals.GetProperty("cookie").GetString());
        Assert.Equal("hu", signals.GetProperty("query").GetString());
    }

    [Fact]
    public void The_defaults_still_read_what_ASP_NET_writes()
    {
        if (Run(".AspNetCore.Culture=c%3Dde%7Cuic%3Dde", "?culture=fr", ".AspNetCore.Culture", "culture") is not { } signals)
        {
            return;
        }

        Assert.Equal("c=de|uic=de", signals.GetProperty("cookie").GetString());
        Assert.Equal("fr", signals.GetProperty("query").GetString());
    }

    // Null when node is not on PATH: the JS-driven check cannot run, and node is not required to build Rask.
    private static JsonElement? Run(params string[] arguments)
    {
        var bundle = Path.Combine(RepoRoot(), "src", "Rask.Wasm", "Browser", "rask.wasm.js");
        Assert.True(File.Exists(bundle), $"Bundle missing: {bundle} — build src/Rask.Wasm first.");

        return NodeFixture.Run("CultureSignalsFixture", [bundle, .. arguments]);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"Could not locate Rask.slnx above {AppContext.BaseDirectory}");
    }
}
