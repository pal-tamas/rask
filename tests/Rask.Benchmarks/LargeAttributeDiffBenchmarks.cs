using System.Text;
using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Benchmarks;

// Measures FrameDiffer.DiffAttributes on an element carrying many attributes (PERF-B). Each changed
// attribute runs FindAttribute, a linear scan of the old attribute list by name — so the diff is
// O(n·m) in (changed × total) attributes. Real elements carry 5-10 attributes; this stress case uses
// 50 (half changed) to size the worst case and decide whether name-indexing the old attributes is
// worth the per-diff dictionary it would cost.
[MemoryDiagnoser]
public partial class LargeAttributeDiffBenchmarks : global::Rask.Core.RaskMarkup
{
    private readonly List<EditOp> _ops = new(64);
    private readonly FrameDiffer.DiffScratch _scratch = new();
    private RenderFrame[] _after = null!;
    private string _afterHtml = "";
    private RenderFrame[] _before = null!;

    [Params(10, 50)] public int AttrCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var before = new Dictionary<string, string?>(AttrCount);
        var after = new Dictionary<string, string?>(AttrCount);
        for (var i = 0; i < AttrCount; i++)
        {
            before["k" + i] = "v" + i;
            after["k" + i] = i % 2 == 0 ? "v" + i : "changed" + i; // half the values change
        }

        _before = FramesOf(Div.Data(before));
        (_after, _afterHtml) = FramesAndHtmlOf(Div.Data(after));
    }

    [Benchmark]
    public int HalfAttributesChanged()
    {
        _ops.Clear();
        FrameDiffer.Diff(_before, _after, _ops, _scratch, out _, _afterHtml);
        return _ops.Count;
    }

    private static RenderFrame[] FramesOf(Component tree) => FramesAndHtmlOf(tree).Frames;

    private static (RenderFrame[] Frames, string Html) FramesAndHtmlOf(Component tree)
    {
        var writer = new FrameWriter();
        var sb = new StringBuilder(8192);
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        var span = writer.WrittenSpan;
        var copy = new RenderFrame[span.Length];
        span.CopyTo(copy);
        return (copy, sb.ToString());
    }
}
