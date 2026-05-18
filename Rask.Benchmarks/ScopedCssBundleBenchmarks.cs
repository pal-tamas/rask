using System.Text;
using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.ScopedCss;
using B = Rask.Benchmarks.Components;

namespace Rask.Benchmarks;

// Two bundle paths are interesting after PR3 lands:
//   (a) GetBundle() on a warm cache — just returns _cachedBundle. Today's served path
//       additionally allocates Encoding.UTF8.GetBytes(css) per HTTP request; PR3 removes
//       that by caching a byte[] alongside.
//   (b) Invalidate -> GetBundle() — rebuilds the bundle (StringBuilder concat + SHA256
//       short hash). Happens on hot-reload and on first registration after a clear.
//
// SimulateServedRequest reproduces today's served path: GetBundle() then UTF-8 encode.
// PR3's GetBundleUtf8() should reduce this to ~zero per-request allocation.
[MemoryDiagnoser]
public class ScopedCssBundleBenchmarks
{
    private Component[] _components = null!;

    [GlobalSetup]
    public void Setup()
    {
        ScopedCssRegistry.InvalidateAll();

        // 8 distinct types, each with a small block of CSS. The registry dedups by type,
        // so we have to use distinct CLR types to populate distinct entries. Each one is
        // a Component instance; the registry reads instance.Css once per type.
        _components =
        [
            B.RegisteredComponentA(), B.RegisteredComponentB(), B.RegisteredComponentC(),
            B.RegisteredComponentD(), B.RegisteredComponentE(), B.RegisteredComponentF(),
            B.RegisteredComponentG(), B.RegisteredComponentH()
        ];

        foreach (var c in _components)
        {
            ScopedCssRegistry.TryRegister(c, out _);
        }
    }

    [Benchmark]
    public string GetBundleCached()
    {
        // Warm-cache read: this is the hot path for the served endpoint. PR3 keeps this
        // allocation-free on the registry side but skips the per-request UTF-8 encode
        // (see SimulateServedRequest).
        var (css, _) = ScopedCssRegistry.GetBundle();
        return css;
    }

    [Benchmark]
    public byte[] SimulateServedRequest()
    {
        // What ServeScopedCssAsync does today: GetBundle() then encode to UTF-8 for the
        // HTTP response body. The per-request UTF-8 encode allocates a fresh byte[] each
        // time. PR3 replaces this with a single cached byte[] inside the registry.
        var (css, _) = ScopedCssRegistry.GetBundle();
        return Encoding.UTF8.GetBytes(css);
    }

    [Benchmark]
    public string InvalidateThenRebuild()
    {
        // Rebuild cost: clears the cached bundle, re-concatenates all entries, recomputes
        // the SHA256 short hash. Worst case is hot-reload of a single Css getter, which
        // fires Invalidate(type) on every saved keystroke.
        foreach (var c in _components)
        {
            ScopedCssRegistry.Invalidate(c.GetType());
            ScopedCssRegistry.TryRegister(c, out _);
        }

        var (css, _) = ScopedCssRegistry.GetBundle();
        return css;
    }
}

// Component subclasses with realistic-shaped CSS. Each one exercises CssScoper.Rewrite
// on registration (Selectors with descendant combinators, pseudo classes, an @media
// nest) so the registry's per-type rewrite cost shows up alongside the bundle concat.

internal sealed class RegisteredComponentA : Component
{
    protected internal override string? Css => """
        .root { display: flex; gap: 8px; }
        .root .item:hover { background: #f0f0f0; }
        @media (max-width: 600px) { .root { flex-direction: column; } }
        """;
}

internal sealed class RegisteredComponentB : Component
{
    protected internal override string? Css => """
        .card { border: 1px solid #ddd; padding: 16px; border-radius: 4px; }
        .card .title { font-weight: bold; margin-bottom: 8px; }
        .card .body { color: #333; line-height: 1.4; }
        """;
}

internal sealed class RegisteredComponentC : Component
{
    protected internal override string? Css => """
        .nav { display: flex; gap: 12px; padding: 8px 16px; }
        .nav a { color: #06c; text-decoration: none; }
        .nav a:hover, .nav a:focus { text-decoration: underline; }
        """;
}

internal sealed class RegisteredComponentD : Component
{
    protected internal override string? Css => """
        .table { width: 100%; border-collapse: collapse; }
        .table th, .table td { padding: 6px 12px; text-align: left; }
        .table tbody tr:nth-child(odd) { background: #fafafa; }
        """;
}

internal sealed class RegisteredComponentE : Component
{
    protected internal override string? Css => """
        .btn { padding: 8px 16px; border-radius: 4px; border: 1px solid transparent; }
        .btn.primary { background: #06c; color: white; }
        .btn:disabled { opacity: 0.5; cursor: not-allowed; }
        """;
}

internal sealed class RegisteredComponentF : Component
{
    protected internal override string? Css => """
        .form { display: flex; flex-direction: column; gap: 8px; }
        .form label { font-size: 0.9rem; color: #555; }
        .form input, .form textarea { padding: 8px; border: 1px solid #ccc; border-radius: 3px; }
        """;
}

internal sealed class RegisteredComponentG : Component
{
    protected internal override string? Css => """
        .alert { padding: 12px 16px; border-radius: 4px; margin: 8px 0; }
        .alert.info { background: #e1f0ff; color: #036; }
        .alert.error { background: #ffe1e1; color: #c00; }
        """;
}

internal sealed class RegisteredComponentH : Component
{
    protected internal override string? Css => """
        .modal { position: fixed; inset: 0; display: grid; place-items: center; }
        .modal .backdrop { position: absolute; inset: 0; background: rgba(0,0,0,0.4); }
        .modal .dialog { position: relative; background: white; padding: 24px; border-radius: 8px; }
        """;
}
