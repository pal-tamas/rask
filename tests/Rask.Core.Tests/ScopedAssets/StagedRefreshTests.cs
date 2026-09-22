using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedAssets;

/// <summary>
///     Covers the staged refresh (<c>BeginCssRefresh</c> / <c>EndCssRefresh</c>) that the
///     hot-reload coordinator wraps around the generated <c>RefreshAll()</c> calls.
///     <para>
///         It replaces a clear-then-repopulate sequence that exposed two windows to any render
///         running concurrently: the scope-id map was empty, so elements were emitted without
///         their <c>data-r-xxxx</c> attribute; and the bundle rebuilt as empty, so
///         <c>&lt;head&gt;</c> carried no stylesheet link and the client morph tore the tag out.
///         The assertions below are deliberately deterministic — they inspect the live state
///         from inside an open refresh window rather than racing a background thread.
///     </para>
/// </summary>
[Collection("ScopedAssets")]
public class StagedRefreshTests
{
    public StagedRefreshTests() => ScopedAssetRegistry.InvalidateAll();

    [Fact]
    public void An_open_refresh_leaves_the_live_scope_ids_fully_intact()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");

        ScopedAssetRegistry.BeginCssRefresh();
        try
        {
            // Mid-refresh, only A has been re-registered so far. A render happening right now must
            // still see BOTH scope ids — this is the window the old clear-first path got wrong.
            ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: green; }");

            Assert.True(ScopedAssetRegistry.HasAnyScopedCss);
            Assert.True(ScopedAssetRegistry.TryGetScopeId(typeof(WidgetA), out var a));
            Assert.True(ScopedAssetRegistry.TryGetScopeId(typeof(WidgetB), out var b));
            Assert.NotEmpty(a);
            Assert.NotEmpty(b);
        }
        finally
        {
            ScopedAssetRegistry.EndCssRefresh();
        }
    }

    [Fact]
    public void An_open_refresh_keeps_serving_the_previous_bundle()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        var before = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);
        Assert.NotEmpty(before);

        ScopedAssetRegistry.BeginCssRefresh();
        try
        {
            // Nothing re-registered yet: the old path would have reported an empty bundle here,
            // which emits a <head> with no stylesheet <link> at all.
            Assert.Equal(before, ScopedAssetRegistry.GetBundleHash(AssetKind.Css));
            Assert.NotNull(ScopedAssetRegistry.GetByHash(before, AssetKind.Css));

            ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: green; }");
            Assert.Equal(before, ScopedAssetRegistry.GetBundleHash(AssetKind.Css));
        }
        finally
        {
            ScopedAssetRegistry.EndCssRefresh();
        }

        Assert.NotEqual(before, ScopedAssetRegistry.GetBundleHash(AssetKind.Css));
    }

    [Fact]
    public void An_open_refresh_does_not_move_the_version_and_the_swap_moves_it_once()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        var version = ScopedAssetRegistry.Version;

        ScopedAssetRegistry.BeginCssRefresh();
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: green; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");
        Assert.Equal(version, ScopedAssetRegistry.Version);

        Assert.True(ScopedAssetRegistry.EndCssRefresh());
        Assert.Equal(version + 1, ScopedAssetRegistry.Version);
    }

    [Fact]
    public void Ending_the_refresh_applies_the_staged_set()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var original));

        ScopedAssetRegistry.BeginCssRefresh();
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: green; }");
        ScopedAssetRegistry.EndCssRefresh();

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var updated));
        Assert.NotEqual(original, updated);
    }

    [Fact]
    public void Ending_the_refresh_reports_a_change_for_a_net_deletion()
    {
        // The delete-only edit: a component's .css file is removed, every surviving sibling
        // re-registers byte-identical content. The old path raised no AssetChanged at all here
        // (bulk invalidate is silent by design, and the unchanged re-registers hit the no-op early
        // return), so nothing repainted and the deleted rules stayed on screen.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");

        ScopedAssetRegistry.BeginCssRefresh();
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");

        Assert.True(ScopedAssetRegistry.EndCssRefresh());
        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.False(ScopedAssetRegistry.TryGetScopeId(typeof(WidgetA), out _));
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetB), out _));
    }

    [Fact]
    public void Ending_the_refresh_reports_no_change_when_the_set_is_identical()
    {
        // Most hot-reload applies touch no CSS at all. Those must not churn the bundle hash, which
        // is an immutable URL the browser has already cached.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        var hash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);

        ScopedAssetRegistry.BeginCssRefresh();
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");

        Assert.False(ScopedAssetRegistry.EndCssRefresh());
        Assert.Equal(hash, ScopedAssetRegistry.GetBundleHash(AssetKind.Css));
    }

    [Fact]
    public void Ending_a_refresh_without_a_begin_does_nothing()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");

        Assert.False(ScopedAssetRegistry.EndCssRefresh());
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
    }

    [Fact]
    public void After_the_refresh_ends_registrations_resume_landing_live()
    {
        // The staging-leak guard. While a refresh is open every registration is diverted, so a
        // coordinator that skipped End (an exception escaping the RefreshAll loop) would silently
        // swallow every later RegisterCss for the life of the process.
        ScopedAssetRegistry.BeginCssRefresh();
        ScopedAssetRegistry.EndCssRefresh();

        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.True(ScopedAssetRegistry.TryGetScopeId(typeof(WidgetA), out _));
    }

    [Fact]
    public void A_css_refresh_leaves_js_untouched()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f(){}");

        ScopedAssetRegistry.BeginCssRefresh();
        ScopedAssetRegistry.EndCssRefresh();

        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.True(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out _));
    }

    [Fact]
    public void A_js_refresh_leaves_css_untouched()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), "export function f(){}");

        ScopedAssetRegistry.BeginJsRefresh();
        Assert.True(ScopedAssetRegistry.EndJsRefresh());

        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
        Assert.False(ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out _));
    }

    [Fact]
    public void Unregistering_during_a_refresh_drops_from_the_staged_set_only()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");

        ScopedAssetRegistry.BeginCssRefresh();
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: green; }");
        ScopedAssetRegistry.UnregisterCss(typeof(WidgetA));

        // Live state is still the pre-refresh one until the swap.
        Assert.True(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));

        Assert.True(ScopedAssetRegistry.EndCssRefresh());
        Assert.False(ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out _));
    }

    [Fact]
    public void Ending_the_refresh_leaves_no_stale_refcounts_behind()
    {
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(WidgetB), ".b { color: blue; }");
        Assert.Equal(2, ScopedAssetRegistry.CssEntryCount);

        ScopedAssetRegistry.BeginCssRefresh();
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".a { color: red; }");
        ScopedAssetRegistry.EndCssRefresh();

        // The staged bucket is built from scratch, so the dropped sibling's entry goes with it
        // rather than lingering at refcount zero.
        Assert.Equal(1, ScopedAssetRegistry.CssEntryCount);
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
