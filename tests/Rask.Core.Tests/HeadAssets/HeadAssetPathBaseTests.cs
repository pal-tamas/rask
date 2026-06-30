using System.Text;
using Rask.Core.HeadAssets;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.HeadAssets;

/// <summary>
///     Verifies that <see cref="HeadAssetRegistry.EmitScopedBundles" /> honours
///     <see cref="LiveOptions.PathBase" />. The scoped bundle URL must carry the configured
///     sub-path prefix (Server / Wasm hosting under a reverse proxy, WASM standalone on GH Pages,
///     etc.) and emit a root-relative URL when the prefix is empty.
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
    public void EmptyPathBase_EmitsRootRelativeBundleUrls()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(Widget), "export function f(){}");
        var cssHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);
        var jsHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedBundles(sb);
        var html = sb.ToString();

        Assert.Contains($"href=\"/_rask/a/{cssHash}.css\"", html);
        Assert.Contains($"src=\"/_rask/a/{jsHash}.js\"", html);
        Assert.DoesNotContain("//_rask/a/", html); // no double slash anywhere
    }

    [Fact]
    public void NonEmptyPathBase_PrependsPrefixToBundleUrls()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(Widget), "export function f(){}");

        LiveOptions.PathBase = "/appA";
        var cssHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);
        var jsHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedBundles(sb);
        var html = sb.ToString();

        Assert.Contains($"href=\"/appA/_rask/a/{cssHash}.css\"", html);
        Assert.Contains($"src=\"/appA/_rask/a/{jsHash}.js\"", html);
        // The bundle morph keys are stable and must not be prefixed.
        Assert.Contains("data-rask-key=\"rsk-css\"", html);
        Assert.Contains("data-rask-key=\"rsk-js\"", html);
    }

    [Fact]
    public void PathBase_NormalizesTrailingSlashOnAssignment()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");

        LiveOptions.PathBase = "/sub/";
        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedBundles(sb);
        var html = sb.ToString();

        Assert.Contains($"href=\"/sub/_rask/a/{hash}.css\"", html);
        Assert.DoesNotContain("//_rask/a/", html);
    }

    [Fact]
    public void PathBase_MultiSegment_Preserved()
    {
        ScopedAssetRegistry.RegisterCss(typeof(Widget), ".x { color: red; }");

        LiveOptions.PathBase = "/a/b";
        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);

        var sb = new StringBuilder();
        HeadAssetRegistry.EmitScopedBundles(sb);
        var html = sb.ToString();

        Assert.Contains($"href=\"/a/b/_rask/a/{hash}.css\"", html);
    }

    private sealed class Widget : Component
    {
        protected override RenderResult Render() => this;
    }
}
