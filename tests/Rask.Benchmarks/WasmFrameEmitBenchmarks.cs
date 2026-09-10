using System.Buffers;
using BenchmarkDotNet.Attributes;

namespace Rask.Benchmarks;

// Guards the per-frame allocation on the WASM outbound path. WasmLiveSession currently
// materializes a byte[] every rendered frame (`_writeBuffer.WrittenSpan.ToArray()`) to both
// (a) hand to the `ApplyRender(byte[])` JSImport and (b) compare against `_lastAppliedPayload`
// for the no-op dedup. The Server host avoids the equivalent copy by double-buffering two
// ArrayBufferWriters and comparing spans directly (LiveSession.cs), sending WrittenMemory to the
// socket with no intermediate array.
//
// This isolates the .NET-side allocation the double-buffer port removes: the ToArray copy
// scales linearly with payload size and happens on every frame; the span compare allocates
// nothing. (The zero-copy hand-off to JS via a MemoryView marshalling is a JS-boundary change
// out of a BDN harness's reach — this captures the managed allocation drop only.)
[MemoryDiagnoser]
public class WasmFrameEmitBenchmarks
{
    private readonly ArrayBufferWriter<byte> _current = new(8192);
    private readonly ArrayBufferWriter<byte> _previous = new(8192);

    // A small keyed-list diff vs a full-document WASM frame — the two ends of the payload range.
    [Params(512, 4096)]
    public int PayloadBytes;

    [GlobalSetup]
    public void Setup()
    {
        // Deterministic identical content in both buffers — the steady-state dedup-hit case,
        // where the current path's only cost is the per-frame byte[] the fix eliminates.
        Span<byte> payload = stackalloc byte[256];
        for (var written = 0; written < PayloadBytes;)
        {
            var chunk = Math.Min(payload.Length, PayloadBytes - written);
            for (var i = 0; i < chunk; i++)
            {
                payload[i] = (byte)((written + i) & 0x7F | 0x20); // printable-ASCII-ish, deterministic
            }

            _current.Write(payload[..chunk]);
            _previous.Write(payload[..chunk]);
            written += chunk;
        }
    }

    // Current WASM path: a fresh byte[] every frame for ApplyRender(byte[]) + the dedup baseline.
    [Benchmark(Baseline = true)]
    public int CurrentToArrayPerFrame() => _current.WrittenSpan.ToArray().Length;

    // Proposed double-buffer path (mirrors Rask.Server): compare the two buffers' spans directly,
    // no per-frame byte[]. Allocation-free.
    [Benchmark]
    public bool DoubleBufferSpanCompare() => _current.WrittenSpan.SequenceEqual(_previous.WrittenSpan);
}
