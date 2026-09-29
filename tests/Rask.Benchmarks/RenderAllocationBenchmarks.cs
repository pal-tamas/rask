using System.Text;
using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Benchmarks;

// The render shapes the other suites leave out, each one a per-render allocation the serializer can
// avoid: text that needs encoding (non-ASCII, an apostrophe, a style with ';'), a children list mixing
// literals with a projection (the `params object?[]` indexer), trusted URLs, and adjacent text children
// rendered with the frame writer on (what every live render does).
[MemoryDiagnoser]
public partial class RenderAllocationBenchmarks : global::Rask.Core.RaskMarkup
{
    private const int Rows = 200;

    private readonly List<EditOp> _ops = new(64);
    private readonly StringBuilder _html = new(64 * 1024);
    private SessionRenderCache _cache = null!;
    private Component _encoded = null!;
    private Component _textRuns = null!;
    private Component _trustedUrls = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cache = new SessionRenderCache();
        _encoded = BuildEncodedTree();
        _trustedUrls = BuildTrustedUrlTree();
        _textRuns = BuildTextRunTree();
    }

    [GlobalCleanup]
    public void Cleanup() => _cache.Dispose();

    [Benchmark]
    public string RenderEncodedText() => _encoded.ToHtml();

    [Benchmark]
    public string RenderTrustedUrls() => _trustedUrls.ToHtml();

    // Built per call: the indexer runs while the tree is built, so building it IS the measured cost.
    [Benchmark]
    public Component BuildMixedChildren()
    {
        var items = Enumerable.Range(0, 8);
        Component last = null!;
        for (var i = 0; i < Rows; i++)
        {
            last = Div["Showing ", items.Select(n => Span.Key(n)[n]), " of ", i];
        }

        return last;
    }

    [Benchmark]
    public bool RenderTextRunsFramed()
    {
        _html.Clear();
        _ops.Clear();
        return _cache.Render(_textRuns, _html, _ops);
    }

    private static Component BuildEncodedTree()
    {
        var rows = new List<Component>(Rows);
        for (var i = 0; i < Rows; i++)
        {
            rows.Add(Div.Class("line").Key(i).Style("display:flex;gap:8px;")[
                Span.Title("Árvíztűrő tükörfúrógép")["Árvíztűrő tükörfúrógép"],
                Span["don't stop"],
                Span["Tom & Jerry"]
            ]);
        }

        return Div.Class("wrap")[rows];
    }

    private static Component BuildTrustedUrlTree()
    {
        var rows = new List<Component>(Rows);
        for (var i = 0; i < Rows; i++)
        {
            rows.Add(A.Key(i).Href(RaskUrl.Trusted("javascript:void(0)")).Class("lnk")["open"]);
        }

        return Div.Class("wrap")[rows];
    }

    private static Component BuildTextRunTree()
    {
        var rows = new List<Component>(Rows);
        for (var i = 0; i < Rows; i++)
        {
            rows.Add(Div.Class("line").Key(i)["Score: ", i, " of ", Rows, " pts"]);
        }

        return Div.Class("wrap")[rows];
    }
}
