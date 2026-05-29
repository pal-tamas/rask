using System.Text;
using Rask.Core.HeadAssets;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.HeadAssets;

/// <summary>
///     Verifies that <see cref="HeadAssetRegistry.EmitMountedAssets" /> honours
///     <see cref="LiveOptions.PathBase" />. The same scoped-asset hash must produce a
///     prefixed <c>href</c>/<c>src</c> when a sub-path is configured (Server / Wasm
///     hosting under a reverse proxy, WASM standalone on GH Pages, etc.) and must
///     emit the legacy root-relative URL when the prefix is empty.
/// </summary>
[Collection("ScopedAssets")]
public sealed class HeadAssetPathBaseTests : IDisposable
{
    private readonly string _priorPathBase;

    public HeadAssetPathBaseTests()
    {
        _priorPathBase = LiveOptions.PathBase;
        ScopedAssetRegistry.InvalidateAll();
        LiveOptions.PathBase = string.Empty;
    }

    public void Dispose()
    {
        LiveOptions.PathBase = _priorPathBase;
        ScopedAssetRegistry.InvalidateAll();
    }

    [Fact]
    public void EmptyPathBase_EmitsLegacyRootRelativeUrls()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(Widget), "export function f(){}");
        ScopedAssetRegistry.TryGetCss(typeof(Widget), out var cssHash);
        ScopedAssetRegistry.TryGetJs(typeof(Widget), out var jsHash);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, new[] { typeof(Widget) });
        var html = sb.ToString();

        Assert.Contains($"href=\"/_rask/a/{cssHash}.css\"", html);
        Assert.Contains($"src=\"/_rask/a/{jsHash}.js\"", html);
        Assert.DoesNotContain("//_rask/a/", html); // no double slash anywhere
    }

    [Fact]
    public void NonEmptyPathBase_PrependsPrefixToCssAndJsUrls()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(Widget), "export function f(){}");
        ScopedAssetRegistry.TryGetCss(typeof(Widget), out var cssHash);
        ScopedAssetRegistry.TryGetJs(typeof(Widget), out var jsHash);

        LiveOptions.PathBase = "/appA";

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, new[] { typeof(Widget) });
        var html = sb.ToString();

        Assert.Contains($"href=\"/appA/_rask/a/{cssHash}.css\"", html);
        Assert.Contains($"src=\"/appA/_rask/a/{jsHash}.js\"", html);
        // The asset-id and data-rask-key payloads must not be re-prefixed.
        Assert.Contains($"data-rask-key=\"rsk-css-{cssHash}\"", html);
        Assert.Contains($"data-rask-key=\"rsk-js-{jsHash}\"", html);
    }

    [Fact]
    public void PathBase_NormalizesTrailingSlashOnAssignment()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(Widget), out var hash);

        LiveOptions.PathBase = "/sub/";

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, new[] { typeof(Widget) });
        var html = sb.ToString();

        Assert.Contains($"href=\"/sub/_rask/a/{hash}.css\"", html);
        Assert.DoesNotContain("//_rask/a/", html);
    }

    [Fact]
    public void PathBase_MultiSegment_Preserved()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(Widget), out var hash);

        LiveOptions.PathBase = "/a/b";

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitMountedAssets(sb, new[] { typeof(Widget) });
        var html = sb.ToString();

        Assert.Contains($"href=\"/a/b/_rask/a/{hash}.css\"", html);
    }

    private sealed class Widget : Component { protected override RenderResult Render() => this; }
}
