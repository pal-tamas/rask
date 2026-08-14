using System.Text;
using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.HeadAssets;
using Rask.Core.ScopedAssets;

namespace Rask.Benchmarks;

/// <summary>
///     Measures the steady-state cost of the per-component scoped-asset pipeline.
///     Three hot paths matter:
///     <list type="bullet">
///         <item>
///             <c>TryGetScopeId</c> — the only by-type registry lookup on the render
///             walk, called (behind a <c>HasAnyScopedCss</c> guard) by
///             <c>LiveRenderContext.PushScope</c> on every user-component entry. A
///             200-component page touches it ~200× per render, so it must stay
///             lock-free and allocation-free.
///         </item>
///         <item>
///             <c>GetByHash</c> — the asset endpoint's only per-request lookup. Bare
///             dictionary access under a single lock; budget is sub-microsecond.
///         </item>
///         <item>
///             <c>EmitScopedBundles</c> (via <c>EmitScopedBundles_Warm</c>) — resolves
///             the two bundle hashes for the <c>&lt;head&gt;</c>, once per render of any
///             live root.
///         </item>
///     </list>
///     <c>TryGetCss</c> / <c>TryGetJs</c> are benchmarked alongside them but have no
///     render-path callers today: head emission resolves the single concatenated bundle
///     by <c>GetBundleHash</c> rather than per mounted type. They are kept measured
///     because they are public API and share the same maps.
///     The remaining benchmarks cover bookkeeping that runs less often but matters
///     for hot-reload latency and publish-time bake throughput: <c>RegisterCss</c>
///     (the cold path the generator-emitted <c>RefreshAll</c> takes) and
///     <c>EnumerateAll</c> (the bake task's snapshot read).
/// </summary>
[MemoryDiagnoser]
public class AssetLoadingBenchmarks
{
    // Stable types used as registry keys across the benchmarks. The fixture
    // declarations live at the bottom of this file; we reuse them per-iteration so
    // every TryGet* / GetByHash hits the lock-free fast paths after warmup.
    private static readonly Type[] _types =
    [
        typeof(AssetRow000), typeof(AssetRow001), typeof(AssetRow002), typeof(AssetRow003),
        typeof(AssetRow004), typeof(AssetRow005), typeof(AssetRow006), typeof(AssetRow007),
        typeof(AssetRow008), typeof(AssetRow009), typeof(AssetRow010), typeof(AssetRow011),
        typeof(AssetRow012), typeof(AssetRow013), typeof(AssetRow014), typeof(AssetRow015),
        typeof(AssetRow016), typeof(AssetRow017), typeof(AssetRow018), typeof(AssetRow019)
    ];

    private string[] _cssHashes = null!;
    private string[] _jsHashes = null!;

    [GlobalSetup]
    public void Setup()
    {
        ScopedAssetRegistry.InvalidateAll();
        _cssHashes = new string[_types.Length];
        _jsHashes = new string[_types.Length];
        for (var i = 0; i < _types.Length; i++)
        {
            ScopedAssetRegistry.RegisterCss(_types[i], $".r{i} {{ color: rgb({i % 256},0,0); }}");
            ScopedAssetRegistry.RegisterJs(_types[i], $"export function f{i}() {{ return {i}; }}");
            ScopedAssetRegistry.TryGetCss(_types[i], out _cssHashes[i]);
            ScopedAssetRegistry.TryGetJs(_types[i], out _jsHashes[i]);
        }

        // Warm the bundle caches so EmitScopedBundles_Warm measures the steady-state emission
        // (two cached-hash reads + two appends), not the one-time concatenation.
        ScopedAssetRegistry.GetBundleHash(AssetKind.Css);
        ScopedAssetRegistry.GetBundleHash(AssetKind.Js);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Hot path: per-render TryGetCss lookups. PushScope calls this once per
    // user-component entry; a 200-component page hits it 200×. Cached hash
    // dictionary keyed by Type — budget sub-microsecond per call.
    // ──────────────────────────────────────────────────────────────────────
    [Benchmark]
    public int TryGetCss_200Lookups()
    {
        var hits = 0;
        for (var i = 0; i < 200; i++)
        {
            if (ScopedAssetRegistry.TryGetCss(_types[i % _types.Length], out _))
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark]
    public int TryGetJs_200Lookups()
    {
        var hits = 0;
        for (var i = 0; i < 200; i++)
        {
            if (ScopedAssetRegistry.TryGetJs(_types[i % _types.Length], out _))
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark]
    public int TryGetScopeId_200Lookups()
    {
        var hits = 0;
        for (var i = 0; i < 200; i++)
        {
            if (ScopedAssetRegistry.TryGetScopeId(_types[i % _types.Length], out _))
            {
                hits++;
            }
        }

        return hits;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Endpoint hot path: GetByHash is what /_rask/a/{hash}.{ext} runs per
    // request. Each call locks, looks up by string-keyed dictionary, returns
    // the cached AssetBytes. A high-throughput page (warm browser cache) won't
    // hit this often, but cold cache + many components fires it N times in
    // parallel.
    // ──────────────────────────────────────────────────────────────────────
    [Benchmark]
    public int GetByHash_Css_200Lookups()
    {
        var hits = 0;
        for (var i = 0; i < 200; i++)
        {
            if (ScopedAssetRegistry.GetByHash(_cssHashes[i % _types.Length], AssetKind.Css) is not null)
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark]
    public int GetByHash_Js_200Lookups()
    {
        var hits = 0;
        for (var i = 0; i < 200; i++)
        {
            if (ScopedAssetRegistry.GetByHash(_jsHashes[i % _types.Length], AssetKind.Js) is not null)
            {
                hits++;
            }
        }

        return hits;
    }

    // ──────────────────────────────────────────────────────────────────────
    // EmitMountedAssets: per-render head emission. Pre-allocated StringBuilder
    // here so the bench reflects the append + lookup cost without the per-call
    // StringBuilder allocation that ApplyTo would do in production. Two scales
    // matter — a "form page" with ~50 source components reusing ~20 distinct
    // types, and a "showcase page" with ~200 source components.
    // ──────────────────────────────────────────────────────────────────────
    // EmitScopedBundles: the per-render <head> emission under the bundle model — reads the two
    // cached bundle hashes off the registry and appends one <link> + one <script>. Constant cost
    // regardless of how many component types are mounted (the old per-component pass scaled with the
    // mounted-type count). Runs once per render of any live root; the bundle bytes themselves are
    // built lazily and cached by registry version, so this warm path never re-concatenates.
    [Benchmark]
    public int EmitScopedBundles_Warm()
    {
        var sb = new StringBuilder(256);
        HeadAssetRegistry.EmitScopedBundles(sb);
        return sb.Length;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Cold path: RegisterCss on a fresh type. Drives hot-reload latency —
    // generator-emitted RefreshAll calls this once per (component, CSS source).
    // Includes the SHA-256 hash + CssScoper.Rewrite cost.
    // ──────────────────────────────────────────────────────────────────────
    [Benchmark]
    public void RegisterCss_FreshType_20Components()
    {
        // Use a NEW set of fresh types each iteration so the registry doesn't
        // short-circuit on "same content already registered". Type identity is
        // stable across iterations, but we Invalidate first so each iteration is
        // a true cold-register.
        ScopedAssetRegistry.InvalidateAllCss();
        for (var i = 0; i < _types.Length; i++)
        {
            ScopedAssetRegistry.RegisterCss(_types[i], $".r{i} {{ color: rgb({i % 256},0,0); padding: {i}px; }}");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Bake path: EnumerateAll snapshots the registry. Once per `dotnet build`
    // of a WASM project; runtime cost only matters for hot-reload-driven
    // re-emits (very rare in production builds).
    // ──────────────────────────────────────────────────────────────────────
    [Benchmark]
    public int EnumerateAll()
    {
        var n = 0;
        foreach (var _ in ScopedAssetRegistry.EnumerateAll())
        {
            n++;
        }

        return n;
    }
}

// 20 distinct subclasses give the registry 20 separate (Type → hash) entries with
// distinct scope ids. The TryGet* benches cycle through these so every iteration
// hits a different bucket — defeating any same-type cache that would mask real
// per-key lookup cost.
#pragma warning disable RASK014
public sealed partial class AssetRow000 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow001 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow002 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow003 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow004 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow005 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow006 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow007 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow008 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow009 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow010 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow011 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow012 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow013 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow014 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow015 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow016 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow017 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow018 : Component
{
    protected override Component? Render() => this;
}

public sealed partial class AssetRow019 : Component
{
    protected override Component? Render() => this;
}
