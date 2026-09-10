using System.Buffers;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace Rask.Benchmarks;

// Measures the per-WS-event decode + JSON parse cost on the server dispatcher
// (Rask.Server/RaskEndpointExtensions.cs RunSocketLoop). The "Legacy" path is
// the pre-PR1 shape: Encoding.UTF8.GetString into a StringBuilder accumulator,
// then JsonDocument.Parse(string). The "Bytes" path is the new shape: write
// fragments into an ArrayBufferWriter<byte> (one-shot messages skip even that
// and parse directly from the receive buffer span), then JsonDocument.Parse
// against ReadOnlyMemory<byte>. Sweeps single-fragment and 4-fragment shapes
// since EndOfMessage is true on the first receive for the common small event.
[MemoryDiagnoser]
public class WsDispatchBenchmarks
{
    private byte[][] _fragments = null!;
    private byte[] _payload = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Representative event: type + handler id + value. Mirrors what
        // rask.js / rask.wasm.js sends on input/click.
        const string json = "{\"type\":\"event\",\"id\":\"h17\",\"value\":\"hello world\"}";
        _payload = Encoding.UTF8.GetBytes(json);

        // Split the same payload into 4 roughly equal fragments to exercise the
        // accumulator branch. WebSockets allow arbitrary fragmentation; 4 is
        // realistic for medium messages.
        var size = (_payload.Length + 3) / 4;
        _fragments = new byte[4][];
        for (var i = 0; i < 4; i++)
        {
            var start = i * size;
            var len = Math.Min(size, _payload.Length - start);
            _fragments[i] = _payload.AsSpan(start, len).ToArray();
        }
    }

    [Benchmark(Baseline = true)]
    public string LegacyString_SingleFragment()
    {
        var sb = new StringBuilder();
        sb.Append(Encoding.UTF8.GetString(_payload, 0, _payload.Length));
        var text = sb.ToString();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    [Benchmark]
    public string Bytes_SingleFragment()
    {
        // Hot path post-PR1: no string decode, no accumulator copy, parse straight
        // from the receive buffer slice.
        using var doc = JsonDocument.Parse(_payload.AsMemory());
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    [Benchmark]
    public string LegacyString_FourFragments()
    {
        var sb = new StringBuilder();
        foreach (var frag in _fragments)
        {
            sb.Append(Encoding.UTF8.GetString(frag, 0, frag.Length));
        }

        var text = sb.ToString();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    [Benchmark]
    public string Bytes_FourFragments()
    {
        var writer = new ArrayBufferWriter<byte>(_payload.Length);
        foreach (var frag in _fragments)
        {
            writer.Write(frag);
        }

        using var doc = JsonDocument.Parse(writer.WrittenMemory);
        return doc.RootElement.GetProperty("id").GetString()!;
    }
}
