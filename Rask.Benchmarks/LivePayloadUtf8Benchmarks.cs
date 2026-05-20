using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;
using Rask.Core.Live;

namespace Rask.Benchmarks;

// Measures the body-injection + extract chain on the server WS frame path.
// Baseline: chained string ops (InjectRootAttr → ExtractBody → BuildPayloadUtf8).
// Candidate: BuildPayloadUtf8WithBody, which encodes html → UTF-8 once, scans on
// ReadOnlySpan<byte> via MemoryExtensions.IndexOf (vectorized), splices in place,
// and writes the JSON in a single pass — no UTF-16 char-by-char loops, no
// intermediate string allocations for the spliced body.
[MemoryDiagnoser]
public class LivePayloadUtf8Benchmarks
{
    private string _html = null!;
    private ArrayBufferWriter<byte> _pooledWriter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pooledWriter = new ArrayBufferWriter<byte>(32 * 1024);

        var rows = new StringBuilder();
        for (var i = 0; i < 200; i++)
        {
            rows.Append("<div class=\"row\" id=\"r").Append(i)
                .Append("\"><span>Item ").Append(i)
                .Append("</span><a href=\"/item/").Append(i)
                .Append("\">open</a><input type=\"text\" value=\"v").Append(i).Append("\"></div>");
        }

        _html = "<!doctype html><html><head><title>Bench</title></head><body>" + rows + "</body></html>";
    }

    [Benchmark(Baseline = true)]
    public byte[] ChainedStringPath()
    {
        var withRoot = LivePayload.InjectRootAttr(_html, "session-bench");
        var body = LivePayload.ExtractBody(withRoot);
        return LivePayload.BuildPayloadUtf8(body, null, false);
    }

    [Benchmark]
    public byte[] BuildPayloadUtf8WithBody()
        => LivePayload.BuildPayloadUtf8WithBody(_html, "session-bench", null, false);

    [Benchmark]
    public int BuildPayloadUtf8WithBody_Pooled()
    {
        // PR2 shape: caller pre-pools the ArrayBufferWriter so the per-frame 4 KiB
        // allocation and the final WrittenSpan.ToArray() copy are both gone. The
        // WebSocket.SendAsync overload accepts ReadOnlyMemory<byte>, so the server
        // dispatcher hands writer.WrittenMemory through directly.
        _pooledWriter.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8WithBody(_pooledWriter, _html, "session-bench", null, false);
        return _pooledWriter.WrittenCount;
    }
}
