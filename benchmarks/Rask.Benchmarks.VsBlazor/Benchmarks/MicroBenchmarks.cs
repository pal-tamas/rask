using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using BenchmarkDotNet.Attributes;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

// Micro_* — hot-path micro-benchmarks for Rask.Core internals. No host, no Component
// tree in the iteration body; all inputs pre-built in [GlobalSetup] so the measured op
// executes only the target hot path. These guard against regressions that broader
// scenarios bury under render-pipeline noise. No Blazor pairing — these isolate Rask's
// own internals; where a baseline ratio is useful, the [Benchmark(Baseline = true)]
// slot holds a stub or equivalent BCL call (e.g. HtmlEncoder.Default.Encode for
// AppendEncoded).

[MemoryDiagnoser]
public class Micro_HtmlSerializerAppendEncodedBenchmarks
{
    private StringBuilder _sb = null!;

    [ParamsSource(nameof(Inputs))] public InputCase Input { get; set; } = null!;

    public static IEnumerable<InputCase> Inputs => new[]
    {
        new InputCase("safe-ascii-16", "data-test-row-42"), new InputCase("safe-ascii-200", new string('x', 200)),
        new InputCase("encoder-fallback", "tom&jerry"), new InputCase("utf-8-multibyte", "árvíztűrő")
    };

    [GlobalSetup]
    public void Setup() => _sb = new StringBuilder(1024);

    [IterationSetup]
    public void Reset() => _sb.Clear();

    [Benchmark(Baseline = true)]
    public void Baseline_HtmlEncoderDefault() => _sb.Append(HtmlEncoder.Default.Encode(Input.Value));

    [Benchmark]
    public void Rask_AppendEncoded() => HtmlSerializer.AppendEncoded(_sb, Input.Value);

    public sealed record InputCase(string Label, string Value)
    {
        public override string ToString() => Label;
    }
}

[MemoryDiagnoser]
public class Micro_FrameDifferDiffBenchmarks
{
    public enum Scenario
    {
        IdentityZeroOps,
        SingleTextChange,
        KeyedSwap50Rows
    }

    private RenderFrame[] _after = null!;

    private RenderFrame[] _before = null!;
    private string? _newHtml;
    private List<EditOp> _output = null!;

    [Params(Scenario.IdentityZeroOps, Scenario.SingleTextChange, Scenario.KeyedSwap50Rows)]
    public Scenario Case { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _output = new List<EditOp>(128);
        switch (Case)
        {
            case Scenario.IdentityZeroOps:
                _before = MicroBenchHarness.BuildFrames(Counter.BuildRask(0));
                _after = MicroBenchHarness.BuildFrames(Counter.BuildRask(0));
                break;
            case Scenario.SingleTextChange:
                _before = MicroBenchHarness.BuildFrames(Counter.BuildRask(0));
                _after = MicroBenchHarness.BuildFrames(Counter.BuildRask(1));
                break;
            case Scenario.KeyedSwap50Rows:
                var orderBefore = new int[50];
                var orderAfter = new int[50];
                for (var i = 0; i < 50; i++)
                {
                    orderBefore[i] = i;
                    orderAfter[i] = i;
                }

                (orderAfter[10], orderAfter[20]) = (orderAfter[20], orderAfter[10]);
                _before = MicroBenchHarness.BuildFrames(KeyedList.BuildRask(orderBefore));
                var pair = MicroBenchHarness.BuildFramesAndHtml(KeyedList.BuildRask(orderAfter));
                _after = pair.Frames;
                _newHtml = pair.Html;
                break;
        }
    }

    [Benchmark]
    public int Rask_FrameDifferDiff()
    {
        _output.Clear();
        return FrameDiffer.Diff(_before, _after, _output, _newHtml);
    }
}

[MemoryDiagnoser]
public class Micro_FrameDifferLisBenchmarks
{
    private int[] _input = null!;

    // O(n²) LIS for keyed reorders. Reverse is the worst case (every pair inversion);
    // the LIS itself is length 1, so the whole array enters the diff as MoveSubtree ops.
    // Identity is the best case (LIS = full array; zero moves). RandomPermutation is the
    // average-case scaling baseline. OneOutOfOrder is the typical real-world shape
    // (a single swap inside an otherwise-sorted list).
    [Params(10, 100, 500, 2000)] public int N { get; set; }

    [Params(MicroBenchHarness.LisShape.Identity, MicroBenchHarness.LisShape.Reverse,
        MicroBenchHarness.LisShape.RandomPermutation, MicroBenchHarness.LisShape.OneOutOfOrder)]
    public MicroBenchHarness.LisShape Shape { get; set; }

    [GlobalSetup]
    public void Setup() => _input = MicroBenchHarness.BuildLisInput(N, Shape);

    [Benchmark]
    public HashSet<int> Rask_ComputeLis() => FrameDiffer.ComputeLisIndexSet(_input);
}

[MemoryDiagnoser]
public class Micro_LivePayloadBuildDiffBenchmarks
{
    private ArrayBufferWriter<byte> _buffer = null!;

    private List<EditOp> _ops = null!;

    [Params(1, 10, 100, 500)] public int OpCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new ArrayBufferWriter<byte>(64 * 1024);
        _ops = new List<EditOp>(OpCount);
        // Mix of op kinds proportional to a typical live diff: 60% SetAttribute (with
        // shared names so the symbol-table path engages above 3 occurrences), 30%
        // UpdateText, 10% RemoveAttribute. Path arrays of depth 4 — representative of a
        // typical Body > Div > Div > {target} site.
        for (var i = 0; i < OpCount; i++)
        {
            var path = new[] { 1, 0, i % 20, i % 5 };
            var bucket = i % 10;
            if (bucket < 6)
            {
                _ops.Add(new EditOp(EditOpKind.SetAttribute, path, "data-shared", $"v{i}"));
            }
            else if (bucket < 9)
            {
                _ops.Add(new EditOp(EditOpKind.UpdateText, path, null, $"text-{i}"));
            }
            else
            {
                _ops.Add(new EditOp(EditOpKind.RemoveAttribute, path, "data-removed", null));
            }
        }
    }

    [Benchmark]
    public int Rask_BuildPayloadUtf8Diff()
    {
        _buffer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8Diff(_buffer, _ops);
        return _buffer.WrittenCount;
    }
}

[MemoryDiagnoser]
public class Micro_FrameWriterGrowthBenchmarks
{
    // FrameWriter rents a RenderFrame[] from ArrayPool<RenderFrame>.Shared with
    // doubling growth. This bench measures the cost of building a frame stream from a
    // fresh writer through N typical open/attribute/close cycles, including any
    // re-rents the growth path triggers. Re-instantiating per iteration is intentional —
    // measures the cold-rent + grow pattern, not steady-state pooled reuse (which is
    // already covered by RaskHarness throughput).
    [Params(10, 100, 1000)] public int Cycles { get; set; }

    [Benchmark]
    public int Rask_FrameWriterGrow()
    {
        var w = new FrameWriter(16);
        for (var i = 0; i < Cycles; i++)
        {
            var idx = w.OpenElement("div", null, false, 0);
            w.Attribute("class", "row");
            w.Attribute("id", "r");
            w.Text("text", 0, 4);
            w.CloseElement(idx, 0);
        }

        return w.Count;
    }
}

[MemoryDiagnoser]
public class Micro_SessionRenderCacheRotateBenchmarks
{
    // Two-frame trivial tree: every iteration runs Seed + diff round-trip, exercising
    // the cache's PrepareCurrentBuffer + TryComputeDiff + RotateBuffers without any
    // meaningful render work. Catches regressions in the buffer-rotation bookkeeping
    // itself (the high-water-mark path of the diff codec — the part that has to stay
    // free of per-render allocation).
    private SessionRenderCache _cache = null!;
    private List<EditOp> _ops = null!;
    private StringBuilder _sb = null!;
    private Component _trivial = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cache = new SessionRenderCache();
        _trivial = Counter.BuildRask(0);
        _sb = new StringBuilder(256);
        _ops = new List<EditOp>(8);
        // Prime the cache so the first measured iteration is a steady-state diff
        // (not the first-render "no previous" early-out).
        RenderInto(_cache.PrepareCurrentBuffer());
        _cache.Snapshot();
    }

    [GlobalCleanup]
    public void Cleanup() => _cache.Dispose();

    [Benchmark]
    public int Rask_PrepareDiffRotate()
    {
        RenderInto(_cache.PrepareCurrentBuffer());
        _cache.TryComputeDiff(_ops);
        return _ops.Count;
    }

    private void RenderInto(FrameWriter writer)
    {
        _sb.Clear();
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(_trivial, _sb);
        }
    }
}
