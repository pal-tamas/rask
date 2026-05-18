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

    [GlobalSetup]
    public void Setup()
    {
        var rows = new System.Text.StringBuilder();
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
}
