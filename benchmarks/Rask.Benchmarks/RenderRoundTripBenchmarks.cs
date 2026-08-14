using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.Live;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks;

// End-to-end-ish: render a representative tree to HTML and build the UTF-8 WS payload —
// the same two hot operations LiveSession.RenderAndSendAsync chains every frame, minus
// the actual WS send. The most representative metric for this PR because it compounds
// the attribute-writer change (RenderHtml) with the payload-build change (BuildPayloadUtf8).
[MemoryDiagnoser]
public partial class RenderRoundTripBenchmarks : global::Rask.Core.RaskMarkup
{
    private Component _tree = null!;

    [GlobalSetup]
    public void Setup() => _tree = BuildTree();

    [Benchmark]
    public byte[] RenderAndBuildPayload()
    {
        var html = _tree.ToHtml();
        var body = LivePayload.ExtractBody(LivePayload.InjectRootAttr(html, "bench"));
        return LivePayload.BuildPayloadUtf8(body, null, false);
    }

    private static Component BuildTree()
    {
        var rows = new List<Component>(15);
        for (var i = 0; i < 15; i++)
        {
            rows.Add(Div.Class("row").Id($"r{i}").Key(i)[
                Span.Class("label")[$"Item {i}"],
                A.Href($"/item/{i}").Class("lnk")[$"open {i}"],
                Input.Value($"v{i}").Type(InputType.Text).Name($"f{i}")
            ]);
        }

        return [
            Doctype,
            Html[
                Body[
                    Div.Class("container").Id("root")[
                        Div.Class("header")[Span["Bench"]],
                        Div.Class("body")[rows]
                    ]
                ]
            ]
        ];
    }
}
