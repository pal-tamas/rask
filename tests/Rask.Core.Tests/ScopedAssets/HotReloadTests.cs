using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedAssets;

/// <summary>
///     Registry-level bookkeeping that hot reload depends on. Critical invariant: per-kind
///     invalidation only clears its own bucket — a CSS hot reload must not blow away JS state and
///     vice versa.
///     <para>
///         The coordinator-driven behaviour (which registries refresh, in what order, and what a
///         concurrent render observes mid-refresh) lives in
///         <c>HotReload/HotReloadPhaseTests</c> and <c>StagedRefreshTests</c>.
///     </para>
/// </summary>
[Collection("ScopedAssets")]
public class HotReloadTests
{
    public HotReloadTests() => ScopedAssetRegistry.InvalidateAll();

    [Fact]
    public void InvalidateAllCss_DropsOnlyCssEntries_FromAssetRegistry()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".y { color: blue; }");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f(){}");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetB), "export function g(){}");

        ScopedAssetRegistry.InvalidateAllCss();

        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out _));
        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out _));
        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetB), out _));
        Assert.Equal(0, ScopedAssetRegistry.CssEntryCount);
        Assert.Equal(2, ScopedAssetRegistry.JsEntryCount);
    }

    [Fact]
    public void InvalidateAllJs_DropsOnlyJsEntries_FromAssetRegistry()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f(){}");

        ScopedAssetRegistry.InvalidateAllJs();

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.False(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out _));
    }

    [Fact]
    public void PerComponentEdit_OnlyAffectedComponentHashChanges()
    {
        // Two components registered; "edit" one's CSS — the other's hash must not change.
        // This is the per-component-invalidation win over the legacy bundle model: hot
        // reload of one .css doesn't bump every other browser's cache key.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hashABefore);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out var hashBBefore);

        // Simulate a file edit to WidgetA's CSS.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: green; }");

        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hashAAfter);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out var hashBAfter);

        Assert.NotEqual(hashABefore, hashAAfter);
        Assert.Equal(hashBBefore, hashBAfter);
    }

    [Fact]
    public void MultiFileEdit_FiresAssetChangedPerEntry()
    {
        // No debounce at the registry level — each register fires its own AssetChanged.
        // The debounce lives on the subscriber side (Rask.Server's
        // SubscribeAssetChangedDebounced); registry stays simple.
        var events = new List<(Type Type, AssetKind Kind)>();
        Action<Type, AssetKind> handler = (t, k) => events.Add((t, k));
        ScopedAssetRegistry.AssetChanged += handler;
        try
        {
            ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
            ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: red; }");
            ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f(){}");
        }
        finally
        {
            ScopedAssetRegistry.AssetChanged -= handler;
        }

        Assert.Contains((typeof(WidgetA), AssetKind.Css), events);
        Assert.Contains((typeof(WidgetB), AssetKind.Css), events);
        Assert.Contains((typeof(WidgetA), AssetKind.Js), events);
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
