using System.Buffers;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

// MemoryGc_* — sustained-load classes. Each [Benchmark] runs N internal cycles per
// BDN op so per-op Allocated/Gen0/Gen1/Gen2 columns surface aggregated pressure
// across many renders. BDN auto-shows Gen1/Gen2 columns once a benchmark allocates
// enough to trigger those collections; one-shot benches (the rest of the suite)
// rarely cross Gen0. (ThreadingDiagnoser was previously enabled here but the BDN
// version misdetects .NET 10 as pre-.NET-Core-3.0 and aborts validation — re-enable
// when BDN ships a fix.)

public abstract class MemoryGcBase
{
    protected BlazorRenderBatchCapture Blazor = null!;
    protected RaskHarness Rask = null!;

    [GlobalCleanup]
    public void Cleanup()
    {
        Rask.Dispose();
        Blazor.Dispose();
    }
}

[MemoryDiagnoser]
public class MemoryGc_SustainedCounterChurnBenchmarks : MemoryGcBase
{
    // 10,000 counter increments on the 200-row stateful page per op. Headline pressure
    // test: this is exactly the load pattern a production live page sees on a busy
    // dashboard (a once-per-frame ticker, a real-time counter, telemetry stream).
    public const int Cycles = 10_000;
    private int _seedCounter;

    private StatefulLargePageWithCounter _stateful = null!;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
#pragma warning disable RASK014
        _stateful = new StatefulLargePageWithCounter();
#pragma warning restore RASK014
        Rask.SeedPrevious(_stateful);
        _seedCounter = 0;
    }

    [Benchmark(Baseline = true)]
    public long Blazor_SustainedCounterChurn()
    {
        var startCounter = _seedCounter;
        _seedCounter += Cycles;
        return Blazor.MeasureSustainedIncrementalUpdates<LargePageWithCounter.BlazorLargePageWithCounter>(
            Cycles,
            i => ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(LargePageWithCounter.BlazorLargePageWithCounter.Counter)] = startCounter + i
            }));
    }

    [Benchmark]
    public long Rask_SustainedCounterChurn()
    {
        long total = 0;
        for (var i = 0; i < Cycles; i++)
        {
            _stateful.Tick();
            total += Rask.RenderAndBuildDiffPayloadBytes(_stateful);
        }

        return total;
    }
}

[MemoryDiagnoser]
public class MemoryGc_KeyedListShufflePressureBenchmarks : MemoryGcBase
{
    public const int Cycles = 5_000;
    private ulong _state;

    private KeyedList.StatefulKeyedList _stateful = null!;

    [Params(500, 2000)] public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
#pragma warning disable RASK014
        _stateful = new KeyedList.StatefulKeyedList { InitialRowCount = N };
#pragma warning restore RASK014
        Rask.SeedPrevious(_stateful);
        _state = 0xC0FFEE_DEADBEEFUL;
    }

    [Benchmark(Baseline = true)]
    public long Blazor_KeyedShufflePressure()
    {
        var localState = _state;
        var n = N;
        var workingOrder = (int[])_stateful.CurrentOrder.Clone();

        var beforeArr = (int[])workingOrder.Clone();
        return Blazor.MeasureSustainedIncrementalUpdates<KeyedList.BlazorKeyedList>(
            Cycles,
            i =>
            {
                if (i == 0)
                {
                    return ParameterView.FromDictionary(new Dictionary<string, object?>
                    {
                        [nameof(KeyedList.BlazorKeyedList.Order)] = beforeArr
                    });
                }

                localState = (localState * 6364136223846793005UL) + 1442695040888963407UL;
                var a = (int)((localState >> 33) % (uint)n);
                localState = (localState * 6364136223846793005UL) + 1442695040888963407UL;
                var b = (int)((localState >> 33) % (uint)n);
                (workingOrder[a], workingOrder[b]) = (workingOrder[b], workingOrder[a]);
                return ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(KeyedList.BlazorKeyedList.Order)] = (int[])workingOrder.Clone()
                });
            });
    }

    [Benchmark]
    public long Rask_KeyedShufflePressure()
    {
        var n = N;
        long total = 0;
        for (var i = 0; i < Cycles; i++)
        {
            _state = (_state * 6364136223846793005UL) + 1442695040888963407UL;
            var a = (int)((_state >> 33) % (uint)n);
            _state = (_state * 6364136223846793005UL) + 1442695040888963407UL;
            var b = (int)((_state >> 33) % (uint)n);
            _stateful.SwapAt(a, b);
            total += Rask.RenderAndBuildDiffPayloadBytes(_stateful);
        }

        return total;
    }
}

[MemoryDiagnoser]
public class MemoryGc_AppendDeletePressureBenchmarks : MemoryGcBase
{
    public const int Cycles = 1_000;
    private int[] _baseOrder = null!;

    private AppendDeleteRowChurn.StatefulAppendDeleteList _stateful = null!;
    private int[] _withInsert = null!;

    [Params(100, 500)] public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        _baseOrder = new int[N];
        for (var i = 0; i < N; i++)
        {
            _baseOrder[i] = i;
        }

        _withInsert = new int[N + 1];
        for (var i = 0; i < N; i++)
        {
            _withInsert[i] = i;
        }

        _withInsert[N] = N + 1000;
#pragma warning disable RASK014
        _stateful = new AppendDeleteRowChurn.StatefulAppendDeleteList { Capacity = N + 1001 };
        _stateful.SetOrder(_baseOrder);
#pragma warning restore RASK014
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_AppendDeletePressure()
    {
        return Blazor.MeasureSustainedIncrementalUpdates<AppendDeleteRowChurn.BlazorAppendDeleteList>(
            Cycles * 2,
            i =>
            {
                var arr = i % 2 == 0 ? _baseOrder : _withInsert;
                return ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(AppendDeleteRowChurn.BlazorAppendDeleteList.Order)] = arr
                });
            });
    }

    [Benchmark]
    public long Rask_AppendDeletePressure()
    {
        long total = 0;
        for (var i = 0; i < Cycles; i++)
        {
            _stateful.SetOrder(_withInsert);
            total += Rask.RenderAndBuildDiffPayloadBytes(_stateful);
            _stateful.SetOrder(_baseOrder);
            total += Rask.RenderAndBuildDiffPayloadBytes(_stateful);
        }

        return total;
    }
}

[global::Rask.Core.RaskMarkup]

[MemoryDiagnoser]
public partial class MemoryGc_DeepTreeMutationPressureBenchmarks : MemoryGcBase
{
    // Same shape as Scale_DeepTreeMutationByDepth at 100-deep — sustained 1,000
    // leaf-text updates per op. Tests the path tracking in FrameDiffer for the
    // worst case (deep, sparse mutation).
    public const int Cycles = 1_000;
    public const int Depth = 100;

    private int _seedCounter;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        Rask.SeedPrevious(Scale_DeepTreeMutationByDepthBenchmarks_BuildHelper(0));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_DeepTreeMutationPressure()
    {
        var startCounter = _seedCounter;
        _seedCounter += Cycles;
        return Blazor
            .MeasureSustainedIncrementalUpdates<Scale_DeepTreeMutationByDepthBenchmarks.ParameterizedBlazorDeepTree>(
                Cycles,
                i => ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(Scale_DeepTreeMutationByDepthBenchmarks.ParameterizedBlazorDeepTree.Counter)] =
                        startCounter + i,
                    [nameof(Scale_DeepTreeMutationByDepthBenchmarks.ParameterizedBlazorDeepTree.Depth)] = Depth
                }));
    }

    [Benchmark]
    public long Rask_DeepTreeMutationPressure()
    {
        long total = 0;
        for (var i = 0; i < Cycles; i++)
        {
            _seedCounter++;
            total += Rask.RenderAndBuildDiffPayloadBytes(
                Scale_DeepTreeMutationByDepthBenchmarks_BuildHelper(_seedCounter));
        }

        return total;
    }

    private static Component Scale_DeepTreeMutationByDepthBenchmarks_BuildHelper(int counter)
    {
        // Mirror the Blazor side exactly: 100-deep div nest wrapping a leaf span,
        // no page shell. Blazor's BuildRenderTree emits no doctype/html/body either.
        var leaf =
            Span.Class("counter")[counter.ToString()];
        for (var i = 0; i < Depth; i++)
        {
            leaf = Div.Class($"d{i}")[leaf];
        }

        return leaf;
    }
}

[MemoryDiagnoser]
public class MemoryGc_PayloadEnvelopePressureBenchmarks
{
    // No Blazor pairing — this is pure Rask internals stress. 10,000 BuildPayloadUtf8Diff
    // calls per op with a pre-built 50-op list (10 ops sharing one attribute name to
    // engage the symbol-table path at LivePayload.cs:279). Catches accumulating
    // dictionary allocations or temp buffer leaks in the encode preamble.
    public const int Cycles = 10_000;
    private ArrayBufferWriter<byte> _buffer = null!;

    private List<EditOp> _ops = null!;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new ArrayBufferWriter<byte>(64 * 1024);
        _ops = new List<EditOp>(50);
        for (var i = 0; i < 50; i++)
        {
            var path = new[] { 1, 0, i % 20, i % 5 };
            if (i < 10)
            {
                _ops.Add(new EditOp(EditOpKind.SetAttribute, path, "data-shared", $"v{i}"));
            }
            else if (i < 40)
            {
                _ops.Add(new EditOp(EditOpKind.UpdateText, path, null, $"text-{i}"));
            }
            else
            {
                _ops.Add(new EditOp(EditOpKind.SetAttribute, path, $"data-unique-{i}", $"v{i}"));
            }
        }
    }

    [Benchmark]
    public long Rask_PayloadEnvelopePressure()
    {
        long total = 0;
        for (var i = 0; i < Cycles; i++)
        {
            _buffer.ResetWrittenCount();
            LivePayload.BuildPayloadUtf8Diff(_buffer, _ops);
            total += _buffer.WrittenCount;
        }

        return total;
    }
}
