using System.Text;
using Rask.Core.HeadAssets;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.HeadAssets;

/// <summary>
///     Covers <see cref="HeadAssetRegistry.EmitMountedAssets" /> — the per-component
///     content-addressed <c>&lt;link&gt;</c>/<c>&lt;script&gt;</c> emission path. Tests
///     drive the method directly (it is not yet wired into
///     <see cref="HeadAssetRegistry.ApplyTo" />; integration lands in a later task).
/// </summary>
[Collection("ScopedAssets")]
public class HeadAssetEmissionTests
{
    public HeadAssetEmissionTests() => ScopedAssetRegistry.InvalidateAll();

    [Fact]
    public void EmptyMountedTypes_EmitsNothing()
    {
        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, Array.Empty<Type>());
        Assert.Equal(0, sb.Length);
    }

    [Fact]
    public void MountedTypeWithNoRegisteredAssets_EmitsNothing()
    {
        // Type is in the mounted set but registry has no entries for it → no tags.
        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, new[] { typeof(NoAssets) });
        Assert.Equal(0, sb.Length);
    }

    [Fact]
    public void CssOnlyComponent_EmitsExactlyOneLinkTag()
    {
        ScopedAssetRegistry.RegisterCss(typeof(CssOnly), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(CssOnly), out var hash);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, new[] { typeof(CssOnly) });
        var html = sb.ToString();

        Assert.Equal(1, CountOccurrences(html, "<link "));
        Assert.Equal(0, CountOccurrences(html, "<script "));
        Assert.Contains($"href=\"/_rask/a/{hash}.css\"", html);
        Assert.Contains($"data-rask-key=\"rsk-css-{hash}\"", html);
        Assert.Contains("rel=\"stylesheet\"", html);
    }

    [Fact]
    public void JsOnlyComponent_EmitsExactlyOneScriptTag_WithDefer()
    {
        ScopedAssetRegistry.RegisterJs(typeof(JsOnly), "export function f() {}");
        ScopedAssetRegistry.TryGetJs(typeof(JsOnly), out var hash);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, new[] { typeof(JsOnly) });
        var html = sb.ToString();

        Assert.Equal(0, CountOccurrences(html, "<link "));
        Assert.Equal(1, CountOccurrences(html, "<script "));
        Assert.Contains($"src=\"/_rask/a/{hash}.js\"", html);
        Assert.Contains($"data-rask-key=\"rsk-js-{hash}\"", html);
        Assert.Contains(" defer ", html);
    }

    [Fact]
    public void BothAssets_EmitsLinkBeforeScript()
    {
        ScopedAssetRegistry.RegisterCss(typeof(BothAssets), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(BothAssets), "export function f() {}");

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, new[] { typeof(BothAssets) });
        var html = sb.ToString();

        var linkPos = html.IndexOf("<link ", StringComparison.Ordinal);
        var scriptPos = html.IndexOf("<script ", StringComparison.Ordinal);
        Assert.True(linkPos >= 0 && scriptPos >= 0);
        Assert.True(linkPos < scriptPos, $"CSS link must precede JS script (link@{linkPos}, script@{scriptPos})");
    }

    [Fact]
    public void TwoComponentsDistinctContent_EmitsTwoDistinctLinks_InIterationOrder()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hashA);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out var hashB);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, new[] { typeof(WidgetA), typeof(WidgetB) });
        var html = sb.ToString();

        Assert.Equal(2, CountOccurrences(html, "<link "));
        var posA = html.IndexOf($"/_rask/a/{hashA}", StringComparison.Ordinal);
        var posB = html.IndexOf($"/_rask/a/{hashB}", StringComparison.Ordinal);
        Assert.True(posA >= 0 && posB >= 0);
        Assert.True(posA < posB, "iteration order should be preserved");
    }

    [Fact]
    public void TwoComponentsHashCollapse_EmitsSingleLink_DedupByHash()
    {
        // CSS that produces byte-equal rewritten output across two types: @font-face is
        // passed through unchanged by CssScoper, so both rewritten payloads match and the
        // content hash is shared.
        const string fontFaceCss = "@font-face { font-family: 'X'; src: url('a.woff2'); }";
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), fontFaceCss);
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), fontFaceCss);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, new[] { typeof(WidgetA), typeof(WidgetB) });
        var html = sb.ToString();

        Assert.Equal(1, CountOccurrences(html, "<link "));
    }

    [Fact]
    public void ThreeComponentsOneHasAssets_EmitsOneTagPair()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetB), "export function f() {}");

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(
            sb, new[] { typeof(WidgetA), typeof(WidgetB), typeof(WidgetC) });
        var html = sb.ToString();

        Assert.Equal(1, CountOccurrences(html, "<link "));
        Assert.Equal(1, CountOccurrences(html, "<script "));
    }

    [Fact]
    public void FrameworkAssetKeyPrefix_IsRskHyphen()
    {
        // The morph-key prefix is documented as "rsk-" — kept stable so user code can
        // safely allocate `data-rask-key` values that don't collide (anything not starting
        // with "rsk-" is in the user namespace).
        Assert.Equal("rsk-", HeadAssetRegistry.FrameworkAssetKeyPrefix);
    }

    [Fact]
    public void AssetUrl_UsesContentAddressedPath_WithLowercaseHexOnly()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, new[] { typeof(WidgetA) });
        var html = sb.ToString();

        // Pattern: href="/_rask/a/{lowercase-hex}.css"
        Assert.Matches("href=\"/_rask/a/[0-9a-f]{12}\\.css\"", html);
        Assert.Contains(hash, html);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => HeadAssetRegistry.EmitMountedAssets(null!, Array.Empty<Type>()));
        Assert.Throws<ArgumentNullException>(() => HeadAssetRegistry.EmitMountedAssets(new StringBuilder(), null!));
    }

    // ─── User Head × per-component asset coexistence ─────────────────────
    //
    // These tests assert the COMPOSED behavior of the existing user-Head emission
    // (HeadAssetRegistry.ApplyTo) combined with the new per-component emission. Since the
    // two paths are not yet integrated, the tests construct the composition manually:
    // user Head first, then EmitMountedAssets appended. Verifies the contract holds in
    // isolation — full pipeline integration follows in a later task.

    [Fact]
    public void UserCdnLink_AppearsBeforePerComponentLink_InCascadeOrder()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Link(Rel: "stylesheet", Href: "https://cdn.example/bootstrap.css"));

        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");

        var html = ComposeUserPlusAssets(registry, new[] { typeof(WidgetA) });

        var cdnPos = html.IndexOf("bootstrap.css", StringComparison.Ordinal);
        var scopedPos = html.IndexOf("/_rask/a/", StringComparison.Ordinal);
        Assert.True(cdnPos >= 0 && scopedPos >= 0);
        Assert.True(cdnPos < scopedPos, "user CDN link must come before scoped link in cascade order");
    }

    [Fact]
    public void UserCdnScript_AppearsBeforePerComponentScript()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Script("https://cdn.example/chartjs.js"));

        ScopedAssetRegistry.RegisterJs(typeof(JsOnly), "export function f() {}");

        var html = ComposeUserPlusAssets(registry, new[] { typeof(JsOnly) });

        var cdnPos = html.IndexOf("chartjs.js", StringComparison.Ordinal);
        var scopedPos = html.IndexOf("/_rask/a/", StringComparison.Ordinal);
        Assert.True(cdnPos >= 0 && scopedPos >= 0);
        Assert.True(cdnPos < scopedPos);
    }

    [Fact]
    public void UserInlineStyle_PreservedVerbatim_NoScopeRewriting()
    {
        // User inline <style> blocks are NOT transformed by CssScoper. The framework only
        // applies scope rewriting to registered scoped-css sibling files, not to inline
        // user content in Head.
        var registry = new HeadAssetRegistry();
        registry.Add(Style()[":root { --accent: hotpink; }"]);

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Contains(":root { --accent: hotpink; }", html);
        // No scope suffix should be appended (would look like `[data-r-xxxx]`).
        Assert.DoesNotContain("[data-r-", html);
    }

    [Fact]
    public void UserInlineScript_PreservedVerbatim_NoDeferInjection()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Script()["window.__inlineRan = true;"]);

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Contains("window.__inlineRan = true;", html);
        // We do not auto-inject defer on user inline scripts.
        Assert.DoesNotContain(" defer ", html);
    }

    [Fact]
    public void SameCdnLink_DeclaredInTwoComponents_DedupedToSingleEmission()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Link(Rel: "stylesheet", Href: "https://cdn.example/bootstrap.css"));
        registry.Add(Link(Rel: "stylesheet", Href: "https://cdn.example/bootstrap.css"));

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Equal(1, CountOccurrences(html, "bootstrap.css"));
    }

    [Fact]
    public void SameCdnUrlDifferentMedia_BothEmitted_ContentHashDifferent()
    {
        // Two <link>s to the same href but with different media queries are semantically
        // distinct (responsive stylesheets). Content hash differs → no dedup.
        var registry = new HeadAssetRegistry();
        registry.Add(Link(Rel: "stylesheet", Href: "https://cdn.example/x.css", Media: "screen"));
        registry.Add(Link(Rel: "stylesheet", Href: "https://cdn.example/x.css", Media: "print"));

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Equal(2, CountOccurrences(html, "<link "));
    }

    [Fact]
    public void PreloadAndStylesheetSameHref_BothEmitted_DifferentRelDoNotDedup()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);

        var registry = new HeadAssetRegistry();
        registry.Add(Link(Rel: "preload", As: "style", Href: $"/_rask/a/{hash}.css"));

        var combined = ComposeUserPlusAssets(registry, new[] { typeof(WidgetA) });

        // Preload link and scoped stylesheet link with the same href are both present
        // (different `rel` attributes → different content hashes → no dedup).
        Assert.Contains("rel=\"preload\"", combined);
        Assert.Contains("rel=\"stylesheet\"", combined);
        // Preload appears before stylesheet (user Head first, scoped emission after).
        var preloadPos = combined.IndexOf("rel=\"preload\"", StringComparison.Ordinal);
        var sheetPos = combined.IndexOf("rel=\"stylesheet\"", StringComparison.Ordinal);
        Assert.True(preloadPos < sheetPos);
    }

    [Fact]
    public void UserSuppliedDataRaskKey_PreservesUserKey()
    {
        // User supplies an explicit data-rask-key; WithRaskKey leaves it alone.
        var registry = new HeadAssetRegistry();
        registry.Add(Link(Rel: "stylesheet",
            Href: "https://cdn.example/x.css",
            Data: new Dictionary<string, string?> { ["rask-key"] = "my-bootstrap" }));

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Contains("data-rask-key=\"my-bootstrap\"", html);
    }

    [Fact]
    public void SingletonDedup_StillWorks_TitleAndBase()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Title("First"));
        registry.Add(Title("Second"));
        registry.Add(Base("/old/"));
        registry.Add(Base("/new/"));

        var html = registry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        Assert.Equal(1, CountOccurrences(html, "<title"));
        Assert.Equal(1, CountOccurrences(html, "<base"));
        Assert.Contains("Second", html); // latest title wins
        Assert.Contains("/new/", html); // latest base wins
    }

    // ─── Helpers and fixtures ────────────────────────────────────────────

    /// <summary>
    ///     Composes user-Head emission via <see cref="HeadAssetRegistry.ApplyTo" /> with
    ///     the new per-component asset emission, in cascade order: user assets first,
    ///     scoped assets after. Simulates the final integrated head layout for tests that
    ///     assert composed behavior before the production pipeline wires both paths.
    /// </summary>
    private static string ComposeUserPlusAssets(
        HeadAssetRegistry userRegistry, IEnumerable<Type> mountedTypes)
    {
        var userHtml = userRegistry.ApplyTo("<head><!--__rask_head_assets__--></head>");
        var scopedSb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(scopedSb, mountedTypes);
        var closeHead = userHtml.IndexOf("</head>", StringComparison.Ordinal);
        return userHtml.Insert(closeHead, scopedSb.ToString());
    }

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

    private sealed class NoAssets : Component
    {
        protected override RenderResult Render() => this;
    }

    private sealed class CssOnly : Component
    {
        protected override RenderResult Render() => this;
    }

    private sealed class JsOnly : Component
    {
        protected override RenderResult Render() => this;
    }

    private sealed class BothAssets : Component
    {
        protected override RenderResult Render() => this;
    }

    private sealed class WidgetA : Component
    {
        protected override RenderResult Render() => this;
    }

    private sealed class WidgetB : Component
    {
        protected override RenderResult Render() => this;
    }

    private sealed class WidgetC : Component
    {
        protected override RenderResult Render() => this;
    }
}
