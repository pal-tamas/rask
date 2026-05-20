using System.Text;
using BenchmarkDotNet.Attributes;
using Rask.Core.Live;

namespace Rask.Benchmarks;

// Measures the JSON payload construction at the WS boundary. BuildPayloadString is the
// pre-change shape (Utf8JsonWriter → MemoryStream.ToArray → Encoding.UTF8.GetString);
// BuildPayloadUtf8 is the new path that skips the UTF-16 round-trip and hands raw bytes
// to WebSocket.SendAsync(ReadOnlyMemory<byte>, …) on the server.
[MemoryDiagnoser]
public class LivePayloadBenchmarks
{
    private string _html = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Approximate ~20 KB of rendered HTML body — representative of a moderately complex page.
        var rows = new StringBuilder();
        for (var i = 0; i < 200; i++)
        {
            rows.Append("<div class=\"row\" id=\"r").Append(i)
                .Append("\"><span>Item ").Append(i)
                .Append("</span><a href=\"/item/").Append(i)
                .Append("\">open</a><input type=\"text\" value=\"v").Append(i).Append("\"></div>");
        }

        _html = "<body data-rask-root=\"x\">" + rows + "</body>";
    }

    [Benchmark(Baseline = true)]
    public string BuildPayloadString() => LivePayload.BuildPayload(_html, null, false);

    [Benchmark]
    public byte[] BuildPayloadUtf8() => LivePayload.BuildPayloadUtf8(_html, null, false);
}
