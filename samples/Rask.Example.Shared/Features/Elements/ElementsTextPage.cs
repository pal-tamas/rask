using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("elements/text")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ElementsTextPage : Component
{
    protected override Component? Head => Title()["Text & inline elements — Rask"];

    protected override Component? Render() =>
    [
        PageHeader.Render(
            "Text & inline elements",
            "Every text-level element: a, abbr, b, bdi, bdo, br, cite, code, data, dfn, del, em, i, ins, "
            + "kbd, mark, q, ruby/rp/rt, s, samp, small, span, strong, sub, sup, time, u, wbr — each a "
            + "generated factory, composed through the [...] children indexer."),
        H2(Class: "h4 mt-4 mb-3")["Live"],
        CodeSample(["ElementsTextDemo.cs"], Result: ElementsTextDemo())
    ];
}
