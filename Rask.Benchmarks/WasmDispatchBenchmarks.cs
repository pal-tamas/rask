using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace Rask.Benchmarks;

// Measures the .NET-side win of PR6's WASM Dispatch byte[] change: the WasmLiveSession
// entry point used to receive a UTF-16 string from the JS interop boundary and call
// JsonDocument.Parse(string); it now receives a byte[] and parses straight from the
// UTF-8 bytes. Mirror of WsDispatchBenchmarks but for the WASM payload — the bigger
// JS-side win (avoiding JSON.stringify into a UTF-16 string + the marshaling-layer
// UTF-16 string copy across the JS/.NET boundary) lives in the browser and is out of
// reach for a BDN harness; this captures the .NET-side allocation drop only.
[MemoryDiagnoser]
public class WasmDispatchBenchmarks
{
    private byte[] _eventBytes = null!;
    private string _eventString = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Representative input event: click on a handler with a small value payload.
        // rask.wasm.js currently fires this shape for click / input / change / submit.
        const string json = "{\"type\":\"event\",\"id\":\"h42\",\"value\":\"hello world\",\"target\":{\"checked\":false}}";
        _eventBytes = Encoding.UTF8.GetBytes(json);
        _eventString = json;
    }

    [Benchmark(Baseline = true)]
    public string LegacyStringPath()
    {
        // Pre-PR6 shape: JSExport marshals UTF-16 string; .NET parses via JsonDocument.Parse(string).
        using var doc = JsonDocument.Parse(_eventString);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    [Benchmark]
    public string BytesPath()
    {
        // Post-PR6 shape: JSExport marshals byte[]; .NET parses via
        // JsonDocument.Parse(ReadOnlyMemory<byte>).
        using var doc = JsonDocument.Parse(_eventBytes.AsMemory());
        return doc.RootElement.GetProperty("id").GetString()!;
    }
}
