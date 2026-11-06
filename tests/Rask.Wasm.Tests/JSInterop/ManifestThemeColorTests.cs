using System.Text.Json;

namespace Rask.Wasm.Tests.JsInteropRuntime;

// Regression guard for the toolbar-tint blink on rask.sh: the WASM manifest injector overwrote the
// page's own <meta name="theme-color"> with the manifest's colour, and the next head morph restored the
// page's — so Safari's tab bar and Chrome-on-Android's address bar flashed between the two on every
// boot. Measured on the published site as #7c3aed -> #512BD4 -> #7c3aed inside ~25ms.
//
// A browser assertion would have to catch one frame of browser CHROME, which no page screenshot shows,
// so this drives the built rask.wasm.js on Jint (see ManifestThemeColorFixture.ts).
public sealed class ManifestThemeColorTests
{
    [Fact]
    public void The_manifest_theme_color_is_a_fallback_never_an_override_of_the_pages_own()
    {
        var bundle = Path.Combine(RepoRoot(), "src", "Rask.Wasm", "Browser", "rask.wasm.js");
        Assert.True(File.Exists(bundle), $"Bundle missing: {bundle} — build src/Rask.Wasm first.");

        var root = ScriptFixture.Run("ManifestThemeColorFixture", bundle);

        // The page said #7c3aed. It must still say exactly that, once — and the manifest still lands.
        Assert.Equal(["#7c3aed"], Contents(root.GetProperty("declared")));
        Assert.True(root.GetProperty("manifestLinked").GetBoolean(), "The manifest <link> was not injected.");

        // A page that names no colour gets the manifest's, which is what the injector is for.
        Assert.Equal(["#512BD4"], Contents(root.GetProperty("undeclared")));

        // A light/dark pair is the page's decision too: neither tag is rewritten, and none is added.
        Assert.Equal(["#ffffff", "#000000"], Contents(root.GetProperty("pair")));
    }

    private static string[] Contents(JsonElement tags) =>
        [.. tags.EnumerateArray().Select(t => t.GetProperty("content").GetString()!)];

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
