using System.Text;
using Rask.Core.HeadAssets;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.HeadAssets;

/// <summary>
///     Covers <see cref="HeadAssetRegistry.EmitScopedBundles" /> — the single scoped-CSS-bundle /
///     scoped-JS-bundle <c>&lt;link&gt;</c>/<c>&lt;script&gt;</c> emission — and its integration into
///     <see cref="HeadAssetRegistry.ApplyTo" /> alongside user <c>Head</c> contributions. Every
///     registered scoped asset of a kind is concatenated by <see cref="ScopedAssetRegistry" /> into a
///     single content-hashed bundle, so the head carries exactly one tag per kind.
/// </summary>
[Collection("ScopedAssets")]
public partial class HeadAssetEmissionTests : global::Rask.Core.RaskMarkup
{
    public HeadAssetEmissionTests() => ScopedAssetRegistry.InvalidateAll();

    [Fact]
    public void NoRegisteredAssets_EmitsNothing()
    {
        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedBundles(sb);
        Assert.Equal(0, sb.Length);
    }

    [Fact]
    public void CssOnly_EmitsExactlyOneLinkTag_AtBundleHash()
    {
        ScopedAssetRegistry.RegisterCss(typeof(CssOnly), ".x { color: red; }");
        var bundleHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedBundles(sb);
        var html = sb.ToString();

        Assert.Equal(1, CountOccurrences(html, "<link "));
        Assert.Equal(0, CountOccurrences(html, "<script "));
        Assert.Contains($"href=\"/_rask/a/{bundleHash}.css\"", html);
        Assert.Contains("data-rask-key=\"rsk-css\"", html);
        Assert.Contains("rel=\"stylesheet\"", html);
    }

    [Fact]
    public void JsOnly_EmitsExactlyOneScriptTag_WithDefer_AtBundleHash()
    {
        ScopedAssetRegistry.RegisterJs(typeof(JsOnly), "export function f() {}");
        var bundleHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedBundles(sb);
        var html = sb.ToString();

        Assert.Equal(0, CountOccurrences(html, "<link "));
        Assert.Equal(1, CountOccurrences(html, "<script "));
        Assert.Contains($"src=\"/_rask/a/{bundleHash}.js\"", html);
        Assert.Contains("data-rask-key=\"rsk-js\"", html);
        Assert.Contains(" defer ", html);
    }

    [Fact]
    public void BothKinds_EmitsLinkBeforeScript()
    {
        ScopedAssetRegistry.RegisterCss(typeof(BothAssets), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(BothAssets), "export function f() {}");

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedBundles(sb);
        var html = sb.ToString();

        var linkPos = html.IndexOf("<link ", StringComparison.Ordinal);
        var scriptPos = html.IndexOf("<script ", StringComparison.Ordinal);
        Assert.True(linkPos >= 0 && scriptPos >= 0);
        Assert.True(linkPos < scriptPos, $"CSS link must precede JS script (link@{linkPos}, script@{scriptPos})");
    }

    [Fact]
    public void ManyCssComponents_CollapseToOneBundleLink()
    {
        // Every registered scoped CSS goes into ONE bundle, so no matter how many components
        // contribute, the head carries a single <link> at the bundle hash.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");
        ScopedAssetRegistry.RegisterCss(typeof(BothAssets), ".c { color: green; }");

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedBundles(sb);
        var html = sb.ToString();

        Assert.Equal(1, CountOccurrences(html, "<link "));
        Assert.Contains($"/_rask/a/{ScopedAssetRegistry.GetBundleHash(AssetKind.Css)}.css", html);
    }

    [Fact]
    public void BundleHash_IsStable_RegardlessOfRegistrationOrder()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");
        var hash1 = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);

        ScopedAssetRegistry.InvalidateAll();
        // Register in the opposite order — the bundle is hash-sorted, so the bytes (and URL) match.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        var hash2 = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void FrameworkAssetKeyPrefix_IsRskHyphen()
    {
        Assert.Equal("rsk-", HeadAssetRegistry.FrameworkAssetKeyPrefix);
    }

    [Fact]
    public void BundleUrl_UsesContentAddressedPath_WithLowercaseHexOnly()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedBundles(sb);
        var html = sb.ToString();

        Assert.Matches("href=\"/_rask/a/[0-9a-f]{12}\\.css\"", html);
    }

    [Fact]
    public void NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HeadAssetRegistry.EmitScopedBundles(null!));
    }

    // ─── User Head × scoped-bundle coexistence (via the integrated ApplyTo) ──────────────

    [Fact]
    public void UserCdnLink_AppearsBeforeScopedBundleLink_InCascadeOrder()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Link.Rel("stylesheet").Href("https://cdn.example/bootstrap.css"));
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");

        var cdnPos = html.IndexOf("bootstrap.css", StringComparison.Ordinal);
        var scopedPos = html.IndexOf("/_rask/a/", StringComparison.Ordinal);
        Assert.True(cdnPos >= 0 && scopedPos >= 0);
        Assert.True(cdnPos < scopedPos, "user CDN link must come before the scoped bundle link");
    }

    [Fact]
    public void UserCdnScript_AppearsBeforeScopedBundleScript()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Script.Src("https://cdn.example/chartjs.js"));
        ScopedAssetRegistry.RegisterJs(typeof(JsOnly), "export function f() {}");

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");

        var cdnPos = html.IndexOf("chartjs.js", StringComparison.Ordinal);
        var scopedPos = html.IndexOf("/_rask/a/", StringComparison.Ordinal);
        Assert.True(cdnPos >= 0 && scopedPos >= 0);
        Assert.True(cdnPos < scopedPos);
    }

    [Fact]
    public void UserInlineStyle_PreservedVerbatim_NoScopeRewriting()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Style[":root { --accent: hotpink; }"]);

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Contains(":root { --accent: hotpink; }", html);
        Assert.DoesNotContain("[data-r-", html);
    }

    [Fact]
    public void UserInlineScript_PreservedVerbatim_NoDeferInjection()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Script["window.__inlineRan = true;"]);

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Contains("window.__inlineRan = true;", html);
        Assert.DoesNotContain(" defer ", html);
    }

    [Fact]
    public void SameCdnLink_DeclaredTwice_DedupedToSingleEmission()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Link.Rel("stylesheet").Href("https://cdn.example/bootstrap.css"));
        registry.Add(Link.Rel("stylesheet").Href("https://cdn.example/bootstrap.css"));

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Equal(1, CountOccurrences(html, "bootstrap.css"));
    }

    [Fact]
    public void SameCdnUrlDifferentMedia_BothEmitted()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Link.Rel("stylesheet").Href("https://cdn.example/x.css").Media("screen"));
        registry.Add(Link.Rel("stylesheet").Href("https://cdn.example/x.css").Media("print"));

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Equal(2, CountOccurrences(html, "<link "));
    }

    [Fact]
    public void UserSuppliedDataRaskKey_PreservesUserKey()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Link
            .Rel("stylesheet")
            .Href("https://cdn.example/x.css")
            .Data(new Dictionary<string, string?> { ["rask-key"] = "my-bootstrap" }));

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Contains("data-rask-key=\"my-bootstrap\"", html);
    }

    [Fact]
    public void SingletonDedup_StillWorks_TitleAndBase()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Title.Id("First"));
        registry.Add(Title.Id("Second"));
        registry.Add(Base.Href("/old/"));
        registry.Add(Base.Href("/new/"));

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Equal(1, CountOccurrences(html, "<title"));
        Assert.Equal(1, CountOccurrences(html, "<base"));
        Assert.Contains("Second", html); // latest title wins
        Assert.Contains("/new/", html); // latest base wins
    }

    // ─── Helpers and fixtures ────────────────────────────────────────────

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }

        return count;
    }

    private sealed class CssOnly : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class JsOnly : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class BothAssets : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class WidgetA : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class WidgetB : Component
    {
        protected override Component? Render() => this;
    }
}
