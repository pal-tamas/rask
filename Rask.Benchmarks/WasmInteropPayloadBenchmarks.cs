using System.Text;
using BenchmarkDotNet.Attributes;
using Rask.Core.Live;

namespace Rask.Benchmarks;

// Measures the WASM live-session payload path that hands a JSON frame across the
// JS interop boundary. Pre-consolidation: InjectRootAttr (string splice via
// UTF-16 char scan) + BuildPayload (Utf8JsonWriter → UTF-8 → string round-trip)
// → 5 string params marshalled into JS. Post-consolidation: a single
// BuildPayloadUtf8WithRoot (UTF-8 byte-span splice + Utf8JsonWriter in one pass)
// → single byte[] handed to JSImport ApplyRender, parsed once in JS.
[MemoryDiagnoser]
public class WasmInteropPayloadBenchmarks
{
    private string _html = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Approximate WASM render output: full document (Doctype + Html + Head + Body),
        // since WASM morphs document.documentElement and needs head children too.
        var rows = new StringBuilder();
        for (var i = 0; i < 200; i++)
        {
            rows.Append("<div class=\"row\" id=\"r").Append(i)
                .Append("\"><span>Item ").Append(i)
                .Append("</span><a href=\"/item/").Append(i)
                .Append("\">open</a><input type=\"text\" value=\"v").Append(i).Append("\"></div>");
        }

        _html = "<!doctype html><html><head><title>Bench</title><link rel=\"stylesheet\" href=\"x.css\"></head><body>"
                + rows
                + "</body></html>";
    }

    [Benchmark(Baseline = true)]
    public string LegacyInjectAndBuildString()
    {
        var withRoot = LivePayload.InjectRootAttr(_html, "wasm");
        return LivePayload.BuildPayload(withRoot, null, false);
    }

    [Benchmark]
    public byte[] BuildPayloadUtf8WithRoot()
        => LivePayload.BuildPayloadUtf8WithRoot(_html, "wasm", null, false);
}
