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
///             <c>TryGetCss</c> / <c>TryGetJs</c> — called by
///             <c>LiveRenderContext.PushScope</c> on every user-component entry during
///             a render walk, and by <c>HeadAssetRegistry.EmitMountedAssets</c> once
///             per mounted type. A 200-component page touches it ~400× per render.
///         </item>
///         <item>
///             <c>GetByHash</c> — the asset endpoint's only per-request lookup. Bare
///             dictionary access under a single lock; budget is sub-microsecond.
///         </item>
///         <item>
///             <c>EmitMountedAssets</c> — builds the <c>&lt;head&gt;</c> per-component
///             tags by iterating mounted types and writing into a <c>StringBuilder</c>.
///             Once per render of any live root.
///         </item>
///     </list>
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

    private readonly HashSet<Type> _mounted200 = new();

    // Pre-built mounted-type sets at two scales. EmitMountedAssets allocates a
    // StringBuilder each call — the bench captures both the alloc and the per-entry
    // append cost.
    private readonly HashSet<Type> _mounted50 = new();

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

        // 50 unique types → 20 distinct types cycled (since _types has 20). Mounted
        // sets are HashSet<Type>, so duplicates collapse — the iteration is over the
        // distinct surviving types either way. The "50" and "200" labels reflect the
        // SOURCE-COMPONENT-COUNT a typical page might have, with the resulting
        // mounted-type-set size capped at 20 (page reuses component types heavily).
        // EmitMountedAssets iterates the HashSet directly, so the cost is bounded by
        // distinct types, not source instances.
        for (var i = 0; i < 50; i++)
        {
            _mounted50.Add(_types[i % _types.Length]);
        }

        for (var i = 0; i < 200; i++)
        {
            _mounted200.Add(_types[i % _types.Length]);
        }
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
    [Benchmark]
    public int EmitMountedAssets_50()
    {
        var sb = new StringBuilder(2048);
        HeadAssetRegistry.EmitMountedAssets(sb, _mounted50);
        return sb.Length;
    }

    [Benchmark]
    public int EmitMountedAssets_200()
    {
        var sb = new StringBuilder(8192);
        HeadAssetRegistry.EmitMountedAssets(sb, _mounted200);
        return sb.Length;
    }

    // ──────────────────────────────────────────────────────────────────────
    // EmitScopedPreloads: the eager-preload pass ApplyTo runs after
    // EmitMountedAssets. The <link rel="preload"> block is render-independent
    // and cached (rebuilt only when the registry mutates), so the steady-state
    // per-render cost is a single Append of the cached string — no rebuild, no
    // per-render registry snapshot. This warm-cache bench captures that cost.
    // ──────────────────────────────────────────────────────────────────────
    [Benchmark]
    public int EmitScopedPreloads_Warm()
    {
        var sb = new StringBuilder(8192);
        HeadAssetRegistry.EmitScopedPreloads(sb);
        return sb.Length;
    }

    // Mirrors what ApplyTo emits per render with the feature on: mounted,
    // render-blocking assets followed by the cached preload block. Compare its
    // Allocated against EmitMountedAssets_200 — the delta is the feature's
    // marginal per-render cost (a single cached-string append; the cache build
    // is amortised to zero after the first render).
    [Benchmark]
    public int EmitMountedAssetsWithPreloads_200()
    {
        var sb = new StringBuilder(8192);
        HeadAssetRegistry.EmitMountedAssets(sb, _mounted200);
        HeadAssetRegistry.EmitScopedPreloads(sb);
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
public sealed class AssetRow000 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow001 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow002 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow003 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow004 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow005 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow006 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow007 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow008 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow009 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow010 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow011 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow012 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow013 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow014 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow015 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow016 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow017 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow018 : Component
{
    protected override RenderResult Render() => this;
}

public sealed class AssetRow019 : Component
{
    protected override RenderResult Render() => this;
}
