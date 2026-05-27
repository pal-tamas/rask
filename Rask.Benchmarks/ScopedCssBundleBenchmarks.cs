using System.Text;
using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.ScopedCss;

namespace Rask.Benchmarks;

// Two bundle paths are interesting:
//   (a) GetBundle() on a warm cache — just returns _cachedBundle. The served path
//       additionally allocates Encoding.UTF8.GetBytes(css) per HTTP request; the
//       UTF-8 cache removes that.
//   (b) Invalidate -> RegisterType() — rebuilds the bundle (StringBuilder concat + SHA256
//       short hash). Happens on hot-reload and on first registration after a clear.
//
// SimulateServedRequest reproduces the old served path: GetBundle() then UTF-8 encode.
// GetBundleUtf8() reduces this to ~zero per-request allocation.
[MemoryDiagnoser]
public class ScopedCssBundleBenchmarks
{
    private const string CssA = """
                                .root { display: flex; gap: 8px; }
                                .root .item:hover { background: #f0f0f0; }
                                @media (max-width: 600px) { .root { flex-direction: column; } }
                                """;

    private const string CssB = """
                                .card { border: 1px solid #ddd; padding: 16px; border-radius: 4px; }
                                .card .title { font-weight: bold; margin-bottom: 8px; }
                                .card .body { color: #333; line-height: 1.4; }
                                """;

    private const string CssC = """
                                .nav { display: flex; gap: 12px; padding: 8px 16px; }
                                .nav a { color: #06c; text-decoration: none; }
                                .nav a:hover, .nav a:focus { text-decoration: underline; }
                                """;

    private const string CssD = """
                                .table { width: 100%; border-collapse: collapse; }
                                .table th, .table td { padding: 6px 12px; text-align: left; }
                                .table tbody tr:nth-child(odd) { background: #fafafa; }
                                """;

    private const string CssE = """
                                .btn { padding: 8px 16px; border-radius: 4px; border: 1px solid transparent; }
                                .btn.primary { background: #06c; color: white; }
                                .btn:disabled { opacity: 0.5; cursor: not-allowed; }
                                """;

    private const string CssF = """
                                .form { display: flex; flex-direction: column; gap: 8px; }
                                .form label { font-size: 0.9rem; color: #555; }
                                .form input, .form textarea { padding: 8px; border: 1px solid #ccc; border-radius: 3px; }
                                """;

    private const string CssG = """
                                .alert { padding: 12px 16px; border-radius: 4px; margin: 8px 0; }
                                .alert.info { background: #e1f0ff; color: #036; }
                                .alert.error { background: #ffe1e1; color: #c00; }
                                """;

    private const string CssH = """
                                .modal { position: fixed; inset: 0; display: grid; place-items: center; }
                                .modal .backdrop { position: absolute; inset: 0; background: rgba(0,0,0,0.4); }
                                .modal .dialog { position: relative; background: white; padding: 24px; border-radius: 8px; }
                                """;

    private (Type Type, string Css)[] _entries = null!;

    [GlobalSetup]
    public void Setup()
    {
        ScopedCssRegistry.InvalidateAll();

        // 8 distinct types, each with a small block of CSS. The registry dedups by type,
        // so we have to use distinct CLR types to populate distinct entries.
        _entries =
        [
            (typeof(RegisteredComponentA), CssA),
            (typeof(RegisteredComponentB), CssB),
            (typeof(RegisteredComponentC), CssC),
            (typeof(RegisteredComponentD), CssD),
            (typeof(RegisteredComponentE), CssE),
            (typeof(RegisteredComponentF), CssF),
            (typeof(RegisteredComponentG), CssG),
            (typeof(RegisteredComponentH), CssH)
        ];

        foreach (var (type, css) in _entries)
        {
            ScopedCssRegistry.RegisterType(type, css);
        }
    }

    [Benchmark]
    public string GetBundleCached()
    {
        // Warm-cache read: this is the hot path for the served endpoint. Allocation-free
        // on the registry side; the served endpoint additionally skips the per-request
        // UTF-8 encode via GetBundleUtf8 (see SimulateServedRequest).
        var (css, _) = ScopedCssRegistry.GetBundle();
        return css;
    }

    [Benchmark]
    public byte[] SimulateServedRequest()
    {
        // The old ServeScopedCssAsync path: GetBundle() then encode to UTF-8 for the
        // HTTP response body. The per-request UTF-8 encode allocates a fresh byte[] each
        // time. GetBundleUtf8 replaces this with a single cached byte[] inside the registry.
        var (css, _) = ScopedCssRegistry.GetBundle();
        return Encoding.UTF8.GetBytes(css);
    }

    [Benchmark]
    public string InvalidateThenRebuild()
    {
        // Rebuild cost: clears the cached bundle, re-concatenates all entries, recomputes
        // the SHA256 short hash. Worst case is hot-reload of a single .css getter, which
        // fires Invalidate(type) on every saved keystroke.
        foreach (var (type, css) in _entries)
        {
            ScopedCssRegistry.Invalidate(type);
            ScopedCssRegistry.RegisterType(type, css);
        }

        var (bundle, _) = ScopedCssRegistry.GetBundle();
        return bundle;
    }
}

internal sealed class RegisteredComponentA : Component
{
    protected override RenderResult Render() => this;
}

internal sealed class RegisteredComponentB : Component
{
    protected override RenderResult Render() => this;
}

internal sealed class RegisteredComponentC : Component
{
    protected override RenderResult Render() => this;
}

internal sealed class RegisteredComponentD : Component
{
    protected override RenderResult Render() => this;
}

internal sealed class RegisteredComponentE : Component
{
    protected override RenderResult Render() => this;
}

internal sealed class RegisteredComponentF : Component
{
    protected override RenderResult Render() => this;
}

internal sealed class RegisteredComponentG : Component
{
    protected override RenderResult Render() => this;
}

internal sealed class RegisteredComponentH : Component
{
    protected override RenderResult Render() => this;
}
