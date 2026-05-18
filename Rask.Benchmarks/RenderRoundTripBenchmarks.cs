using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Live;
using C = Rask.Core.Components.Components;

namespace Rask.Benchmarks;

// End-to-end-ish: render a representative tree to HTML and build the UTF-8 WS payload —
// the same two hot operations LiveSession.RenderAndSendAsync chains every frame, minus
// the actual WS send. The most representative metric for this PR because it compounds
// the attribute-writer change (RenderHtml) with the payload-build change (BuildPayloadUtf8).
[MemoryDiagnoser]
public class RenderRoundTripBenchmarks
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
        var rows = new List<Child>(capacity: 15);
        for (var i = 0; i < 15; i++)
        {
            rows.Add(C.Div(Class: "row", Id: $"r{i}")[
                C.Span(Class: "label")[$"Item {i}"],
                C.A(Href: $"/item/{i}", Class: "lnk")[$"open {i}"],
                C.Input(Type: "text", Name: $"f{i}", Value: $"v{i}")
            ]);
        }

        return C.Fragment()[
            C.Doctype(),
            C.Html()[
                C.Body()[
                    C.Div(Class: "container", Id: "root")[
                        C.Div(Class: "header")[C.Span()["Bench"]],
                        C.Div(Class: "body")[rows]
                    ]
                ]
            ]
        ];
    }
}
