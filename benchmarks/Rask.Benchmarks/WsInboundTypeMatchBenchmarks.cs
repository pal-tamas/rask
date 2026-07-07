using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace Rask.Benchmarks;

// The inbound WS dispatch (Rask.Server/RaskEndpointExtensions.cs) matches each frame's "type" against
// four constants to route it. The legacy path called JsonElement.GetString() — a fresh string per
// frame — only to `==` those literals; the new path uses JsonElement.ValueEquals(...u8), which compares
// the UTF-8 bytes in place with zero allocation. This runs on EVERY inbound frame (every keystroke,
// 60 Hz scroll tick, click), so the per-frame string is pure waste. Both approaches live here so a
// single run shows the allocation delta directly.
[MemoryDiagnoser]
public class WsInboundTypeMatchBenchmarks
{
    private JsonDocument _doc = null!;
    private JsonElement _type;

    [GlobalSetup]
    public void Setup()
    {
        // A representative frame — a navigate, matching the second of the four type literals.
        _doc = JsonDocument.Parse("{\"type\":\"navigate\",\"url\":\"/products/42\"}"u8.ToArray());
        _type = _doc.RootElement.GetProperty("type");
    }

    [GlobalCleanup]
    public void Cleanup() => _doc.Dispose();

    [Benchmark(Baseline = true)]
    public bool LegacyString_GetStringCompare()
    {
        var type = _type.GetString();
        return type == "hello" || type == "navigate" || type == "jsResult" || type == "dotNetInvoke";
    }

    [Benchmark]
    public bool Bytes_ValueEquals() =>
        _type.ValueEquals("hello"u8) || _type.ValueEquals("navigate"u8)
        || _type.ValueEquals("jsResult"u8) || _type.ValueEquals("dotNetInvoke"u8);
}
