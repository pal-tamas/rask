using System.Reflection;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedAssets;

/// <summary>
///     Covers hot-reload coordination over <see cref="ScopedAssetRegistry" />. Critical
///     invariant: per-kind invalidation only clears its own bucket — a CSS hot-reload
///     must not blow away JS state and vice versa.
/// </summary>
[Collection("ScopedAssets")]
public class HotReloadTests
{
    public HotReloadTests() => ScopedAssetRegistry.InvalidateAll();

    [Fact]
    public void CssHotReload_ClearsCss_LeavesJsIntact()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f(){}");
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out _));

        InvokeUpdateApplication(
            "Rask.Core.ScopedCss.ScopedCssHotReloadHandler",
            new[] { typeof(__RaskScopedCssRegistration) });

        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out _));
    }

    [Fact]
    public void JsHotReload_ClearsJs_LeavesCssIntact()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f(){}");

        InvokeUpdateApplication(
            "Rask.Core.ScopedJs.ScopedJsHotReloadHandler",
            new[] { typeof(__RaskScopedJsRegistration) });

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.False(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out _));
    }

    [Fact]
    public void CssHotReload_ClearsScopedAssetRegistryCss()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");

        InvokeUpdateApplication(
            "Rask.Core.ScopedCss.ScopedCssHotReloadHandler",
            new[] { typeof(__RaskScopedCssRegistration) });

        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
    }

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

    [Fact]
    public void DeletedSibling_AfterHotReload_LeavesNoStaleRegistryEntry()
    {
        // When a .css sibling is deleted, the generator re-emits __RaskScopedCssRegistration
        // without the deleted pair. RefreshAll re-runs over surviving pairs only — if the
        // hot-reload handler didn't clear first, the deleted component's entry would
        // persist. This test asserts the clear happens.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: red; }");

        // Simulate generator re-emit: invalidate then re-register only WidgetB (WidgetA's
        // sibling was "deleted").
        InvokeUpdateApplication(
            "Rask.Core.ScopedCss.ScopedCssHotReloadHandler",
            new[] { typeof(__RaskScopedCssRegistration) });
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: red; }");

        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out _));
    }

    private static void InvokeUpdateApplication(string handlerFullName, Type[]? types)
    {
        var handlerType = typeof(ScopedAssetRegistry).Assembly
            .GetType(handlerFullName, true)!;
        var update = handlerType.GetMethod(
            "UpdateApplication",
            BindingFlags.Public | BindingFlags.Static)!;
        update.Invoke(null, new object?[] { types });
    }

    private sealed class WidgetA : Component
    {
        protected override RenderResult Render() => this;
    }

    private sealed class WidgetB : Component
    {
        protected override RenderResult Render() => this;
    }

    // Sentinel types whose Name matches the generator-emitted classes — the hot-reload
    // handler's gate is name-based, so a stand-in is enough for tests.
    private sealed class __RaskScopedCssRegistration
    {
    }

    private sealed class __RaskScopedJsRegistration
    {
    }
}
