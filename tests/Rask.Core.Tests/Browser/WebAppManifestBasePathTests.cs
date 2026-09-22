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
    public void A_base_path_roots_the_default_start_url_and_scope_at_the_root()
    {
        var root = Parse(new WebAppManifest { Name = "App" }.ToJson(""));

        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.Equal("/", root.GetProperty("scope").GetString());
    }

    [Fact]
    public void A_base_path_roots_relative_urls_under_a_sub_path_deploy()
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
    public void A_base_path_resolves_relative_segments_and_keeps_queries()
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
    public void A_base_path_leaves_absolute_urls_untouched(string url)
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
    public void A_base_path_rewrites_share_target_and_file_handler_actions()
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
    public void ToJson_without_a_base_path_leaves_relative_urls_verbatim()
    {
        var root = Parse(new WebAppManifest { Name = "App" }.ToJson());

        // The WASM host resolves these against <base> at boot, so the serialized form stays relative.
        Assert.Equal(".", root.GetProperty("start_url").GetString());
        Assert.Equal(".", root.GetProperty("scope").GetString());
    }
}
