using System.Text.Json;
using Rask.Core.Browser;

namespace Rask.Core.Tests.Browser;

// ToJson(basePath) roots a manifest's relative URLs at the app base path so the manifest can be served
// from its own endpoint on the Server host (where members would otherwise resolve against that endpoint
// URL). This mirrors the WASM host's boot-time abs() step. The no-arg ToJson() leaves URLs verbatim.
public class WebAppManifestBasePathTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ToJson_BasePath_RootsDefaultStartUrlAndScopeAtRoot()
    {
        var root = Parse(new WebAppManifest { Name = "App" }.ToJson(""));

        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.Equal("/", root.GetProperty("scope").GetString());
    }

    [Fact]
    public void ToJson_BasePath_RootsRelativeUrlsUnderSubPathDeploy()
    {
        var manifest = new WebAppManifest
        {
            Name = "App",
            StartUrl = ".",
            Scope = ".",
            Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml")]
        };

        var root = Parse(manifest.ToJson("/app"));

        Assert.Equal("/app/", root.GetProperty("start_url").GetString());
        Assert.Equal("/app/", root.GetProperty("scope").GetString());
        Assert.Equal("/app/icon.svg", root.GetProperty("icons")[0].GetProperty("src").GetString());
    }

    [Fact]
    public void ToJson_BasePath_ResolvesRelativeSegmentsAndKeepsQueries()
    {
        var manifest = new WebAppManifest
        {
            Name = "App",
            StartUrl = "?source=pwa",
            Shortcuts = [new ManifestShortcut("Browser APIs", "browser/clipboard")]
        };

        var root = Parse(manifest.ToJson("/app"));

        Assert.Equal("/app/?source=pwa", root.GetProperty("start_url").GetString());
        Assert.Equal("/app/browser/clipboard", root.GetProperty("shortcuts")[0].GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("/already/rooted")]
    [InlineData("https://cdn.example.com/icon.svg")]
    [InlineData("//cdn.example.com/icon.svg")]
    public void ToJson_BasePath_LeavesAbsoluteUrlsUntouched(string url)
    {
        var manifest = new WebAppManifest
        {
            Name = "App",
            Icons = [new ManifestIcon(url, "any", "image/svg+xml")]
        };

        var root = Parse(manifest.ToJson("/app"));

        Assert.Equal(url, root.GetProperty("icons")[0].GetProperty("src").GetString());
    }

    [Fact]
    public void ToJson_BasePath_RewritesShareTargetAndFileHandlerActions()
    {
        var manifest = new WebAppManifest
        {
            Name = "App",
            ShareTarget = new ShareTarget("share", new ShareTargetParams(Title: "title")),
            FileHandlers = [new FileHandler("open", new Dictionary<string, string[]> { ["text/csv"] = [".csv"] })]
        };

        var root = Parse(manifest.ToJson("/app"));

        Assert.Equal("/app/share", root.GetProperty("share_target").GetProperty("action").GetString());
        Assert.Equal("/app/open", root.GetProperty("file_handlers")[0].GetProperty("action").GetString());
    }

    [Fact]
    public void ToJson_NoArg_LeavesRelativeUrlsVerbatim()
    {
        var root = Parse(new WebAppManifest { Name = "App" }.ToJson());

        // The WASM host resolves these against <base> at boot, so the serialized form stays relative.
        Assert.Equal(".", root.GetProperty("start_url").GetString());
        Assert.Equal(".", root.GetProperty("scope").GetString());
    }
}
