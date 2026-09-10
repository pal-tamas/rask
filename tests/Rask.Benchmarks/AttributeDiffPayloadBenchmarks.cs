using System.Buffers;
using BenchmarkDotNet.Attributes;
using Rask.Core.Live;

namespace Rask.Benchmarks;

// Isolates the attribute-name symbol-table cost in LivePayload.BuildPayloadUtf8Diff. Ops are
// pre-built in [GlobalSetup] and the output buffer is reused (cleared per call), so each body
// measures only the payload encode + the dedup pass.
//   - SmallAttributeDiff: 2 SetAttribute ops — the common reactive update. With fewer than 3 ops
//     no name can reach the interning break-even, so the symbol-table pass (and its count-map
//     allocation) is skipped entirely.
//   - AttributeBurst: 100 SetAttribute ops sharing one name — exercises interning; the count/index
//     maps are reused per-thread across renders rather than reallocated every frame.
[MemoryDiagnoser]
public class AttributeDiffPayloadBenchmarks
{
    private readonly ArrayBufferWriter<byte> _buffer = new(4096);
    private List<EditOp> _burst = null!;
    private List<EditOp> _small = null!;

    [GlobalSetup]
    public void Setup()
    {
        _small = new List<EditOp>
        {
            new(EditOpKind.SetAttribute, new[] { 0 }, "class", "is-active"),
            new(EditOpKind.SetAttribute, new[] { 1 }, "value", "42")
        };

        _burst = new List<EditOp>(100);
        for (var i = 0; i < 100; i++)
        {
            _burst.Add(new EditOp(EditOpKind.SetAttribute, new[] { i }, "class", "row-" + i));
        }
    }

    [Benchmark]
    public int SmallAttributeDiff()
    {
        _buffer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8Diff(_buffer, _small);
        return _buffer.WrittenCount;
    }

    [Benchmark]
    public int AttributeBurst()
    {
        _buffer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8Diff(_buffer, _burst);
        return _buffer.WrittenCount;
    }
}
