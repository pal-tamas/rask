using System.Text;
using Rask.Core.HeadAssets;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;
using Rask.TestSupport;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.HeadAssets;

/// <summary>
///     Covers <see cref="HeadAssetRegistry.EmitScopedPreloads" /> — the eager prefetch pass that
///     emits one low-priority <c>&lt;link rel="prefetch"&gt;</c> per <em>registered</em> scoped
///     asset (not just the mounted ones), so a later mount finds its stylesheet/script already
///     cached and swaps with no FOUC. Gated by <see cref="LiveOptions.PreloadScopedAssets" />
///     (default on); the markup is cached and only rebuilt when the registry mutates
///     (<see cref="ScopedAssetRegistry.Version" />) or the URL prefix changes.
/// </summary>
[Collection("ScopedAssets")]
public sealed class ScopedPreloadEmissionTests : IDisposable
{
    private readonly bool _priorPreload;
    private readonly string _priorPathBase;

    public ScopedPreloadEmissionTests()
    {
        _priorPreload = LiveOptions.PreloadScopedAssets;
        _priorPathBase = LiveOptions.PathBase;
        ScopedAssetRegistry.InvalidateAll();
        LiveOptions.PathBase = string.Empty;
        LiveOptions.PreloadScopedAssets = true;
    }

    public void Dispose()
    {
        LiveOptions.PreloadScopedAssets = _priorPreload;
        LiveOptions.PathBase = _priorPathBase;
        ScopedAssetRegistry.InvalidateAll();
    }

    [Fact]
    public void Disabled_EmitsNothing()
    {
        LiveOptions.PreloadScopedAssets = false;
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedPreloads(sb);

        Assert.Equal(0, sb.Length);
    }

    [Fact]
    public void Enabled_EmitsPrefetchLinkForCss()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);

        var html = Emit();

        Assert.Contains(
            $"<link rel=\"prefetch\" as=\"style\" href=\"/_rask/a/{hash}.css\" " +
            $"data-rask-key=\"rsk-prefetch-css-{hash}\">",
            html);
        // Prefetch links must be inert to the cascade and the client invoke gate: never a
        // render-blocking stylesheet, never a deferred script.
        Assert.DoesNotContain("rel=\"stylesheet\"", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void Enabled_EmitsPrefetchLinkForJs()
    {
        ScopedAssetRegistry.RegisterJs(typeof(JsOnly), "export function f() {}");
        ScopedAssetRegistry.TryGetJs(typeof(JsOnly), out var hash);

        var html = Emit();

        Assert.Contains(
            $"<link rel=\"prefetch\" as=\"script\" href=\"/_rask/a/{hash}.js\" " +
            $"data-rask-key=\"rsk-prefetch-js-{hash}\">",
            html);
        Assert.DoesNotContain(" defer ", html);
    }

    [Fact]
    public void Enabled_PrefetchesEveryRegisteredAsset_NotJustOne()
    {
        // The prefetch pass takes no mounted-types argument — it covers the whole registry, so a
        // component that is registered but not on the current route is still warmed.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hashA);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out var hashB);

        var html = Emit();

        Assert.Contains($"rsk-prefetch-css-{hashA}", html);
        Assert.Contains($"rsk-prefetch-css-{hashB}", html);
        Assert.Equal(2, CountOccurrences(html, "rel=\"prefetch\""));
    }

    [Fact]
    public void Enabled_EmitsCssPrefetchesBeforeJsPrefetches()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(JsOnly), "export function f() {}");

        var html = Emit();

        var stylePos = html.IndexOf("as=\"style\"", StringComparison.Ordinal);
        var scriptPos = html.IndexOf("as=\"script\"", StringComparison.Ordinal);
        Assert.True(stylePos >= 0 && scriptPos >= 0);
        Assert.True(stylePos < scriptPos, "CSS prefetches should precede JS prefetches");
    }

    [Fact]
    public void Enabled_HonoursPathBasePrefix()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);

        LiveOptions.PathBase = "/appA";
        var html = Emit();

        Assert.Contains($"href=\"/appA/_rask/a/{hash}.css\"", html);
        Assert.DoesNotContain("//_rask/a/", html);
        // The data-rask-key payload must not be re-prefixed.
        Assert.Contains($"data-rask-key=\"rsk-prefetch-css-{hash}\"", html);
    }

    [Fact]
    public void Cached_RepeatedEmitsAreByteIdentical()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");

        Assert.Equal(Emit(), Emit());
    }

    [Fact]
    public void Cache_RebuildsAfterRegistryMutation()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hashA);
        var first = Emit();
        Assert.Contains($"rsk-prefetch-css-{hashA}", first);
        Assert.Equal(1, CountOccurrences(first, "rel=\"prefetch\""));

        // Registering a second asset bumps ScopedAssetRegistry.Version → the cached block is
        // recomputed and now covers both assets.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out var hashB);
        var second = Emit();

        Assert.Contains($"rsk-prefetch-css-{hashA}", second);
        Assert.Contains($"rsk-prefetch-css-{hashB}", second);
    }

    [Fact]
    public void ApplyTo_EmitsPrefetchForOffRouteAsset_AlongsideMountedStylesheet()
    {
        // End-to-end through the real head pipeline: CssOnly is mounted; WidgetA is registered
        // but off-route. The mounted type gets a render-blocking stylesheet; both types get a
        // low-priority prefetch so navigating to WidgetA later hits a warm cache (no FOUC).
        ScopedAssetRegistry.RegisterCss(typeof(CssOnly), ".m { color: green; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".o { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(CssOnly), out var mountedHash);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var offRouteHash);

        var view = new StubComponent(new CssOnly());
        using var ctx = LiveRenderContext.Begin(view);
        var serialized = new StringBuilder();
        HtmlSerializer.Serialize(view, serialized);

        var html = new HeadAssetRegistry().ApplyTo("<head><!--__rask_head_assets__--></head>");

        Assert.Contains($"data-rask-key=\"rsk-css-{mountedHash}\"", html);
        Assert.Contains($"rsk-prefetch-css-{mountedHash}", html);
        Assert.Contains($"rsk-prefetch-css-{offRouteHash}", html);
        // Cascade order: the render-blocking stylesheet precedes the low-priority prefetch block.
        var sheetPos = html.IndexOf("rel=\"stylesheet\"", StringComparison.Ordinal);
        var prefetchPos = html.IndexOf("rel=\"prefetch\"", StringComparison.Ordinal);
        Assert.True(sheetPos >= 0 && prefetchPos >= 0 && sheetPos < prefetchPos);
    }

    [Fact]
    public void NullArgument_Throws() =>
        Assert.Throws<ArgumentNullException>(() => HeadAssetRegistry.EmitScopedPreloads(null!));

    private static string Emit()
    {
        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedPreloads(sb);
        return sb.ToString();
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

    private sealed class WidgetA : Component
    {
        protected override RenderResult Render() => this;
    }

    private sealed class WidgetB : Component
    {
        protected override RenderResult Render() => this;
    }

    private sealed class JsOnly : Component
    {
        protected override RenderResult Render() => this;
    }

    // Rendered (not just registered) by the ApplyTo integration test, so it must return a real
    // element rather than `this`.
    private sealed class CssOnly : Component
    {
        protected override RenderResult Render() => Div();
    }
}
