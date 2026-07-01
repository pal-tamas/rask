using System.Text;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedAssets;

// The scoped-CSS bundle is minified (before hashing) when LiveOptions.MinifyScopedAssets is on. These pin
// that the flag drives it, that flipping it rebuilds the bundle, that it's deterministic, and that the JS
// bundle is never minified. Runs in the non-parallel ScopedAssets collection because both the registry and
// LiveOptions.MinifyScopedAssets are process-global.
[Collection("ScopedAssets")]
public class ScopedAssetBundleMinifyTests : IDisposable
{
    private const string PrettyCss = ".card {\n    color: red;\n    /* a comment */\n    margin: 0;\n}\n";

    public ScopedAssetBundleMinifyTests() => ScopedAssetRegistry.InvalidateAll();

    public void Dispose()
    {
        ScopedAssetRegistry.InvalidateAll();
        LiveOptions.MinifyScopedAssets = null; // don't leak the flag to sibling tests in the collection
    }

    private static string CssBundle()
    {
        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);
        var bytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value;
        return Encoding.UTF8.GetString(bytes.Utf8.Span);
    }

    [Fact]
    public void MinifyOn_ProducesSmallerBundleWithNoCommentsOrNewlines()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), PrettyCss);

        LiveOptions.MinifyScopedAssets = false;
        var raw = CssBundle();

        LiveOptions.MinifyScopedAssets = true;
        var min = CssBundle();

        Assert.Contains("\n", raw);              // unminified keeps the source formatting
        Assert.DoesNotContain("\n", min);        // minified collapses it
        Assert.DoesNotContain("/*", min);        // and drops comments
        Assert.True(min.Length < raw.Length);
        Assert.Contains("color:", min);          // ...without losing the actual rules
    }

    [Fact]
    public void FlippingFlag_RebuildsBundle_HashChanges()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), PrettyCss);

        LiveOptions.MinifyScopedAssets = false;
        var rawHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);

        LiveOptions.MinifyScopedAssets = true;
        var minHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);

        Assert.NotEqual(rawHash, minHash); // the minified bytes hash differently → a fresh immutable URL
    }

    [Fact]
    public void Bundle_IsDeterministic_ForSameInputAndFlag()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), PrettyCss);
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".btn { padding: 2px; }");
        LiveOptions.MinifyScopedAssets = true;

        var first = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);
        ScopedAssetRegistry.InvalidateAll();
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), PrettyCss);
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".btn { padding: 2px; }");
        var second = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);

        Assert.Equal(first, second); // byte-stable → immutable URL doesn't churn between builds
    }

    [Fact]
    public void JsBundle_IsNeverMinified()
    {
        const string prettyJs = "export function f() {\n    return 1;   /* keep me */\n}\n";
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), prettyJs);
        LiveOptions.MinifyScopedAssets = true;

        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);
        var js = Encoding.UTF8.GetString(ScopedAssetRegistry.GetByHash(hash, AssetKind.Js)!.Value.Utf8.Span);

        Assert.Contains("\n", js);      // JS is served as-is even with the flag on
        Assert.Contains("/* keep me */", js);
    }

    private sealed class WidgetA : Component
    {
        protected override RenderResult Render() => Div();
    }

    private sealed class WidgetB : Component
    {
        protected override RenderResult Render() => Div();
    }
}
