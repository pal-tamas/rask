using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("elements/metadata")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ElementsMetadataPage : Component
{
    protected override RenderResult Head => Title()["Document & metadata elements — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Document & metadata elements",
            "The page-shell elements: html, head, body, title, base, link, meta, style, script, noscript, "
            + "plus template & slot. These build the document, so they're shown via serialized output "
            + "rather than rendered inside this page (a real app declares head content via the Head property)."),
        H2(Class: "h4 mt-4 mb-3")["The shell + template/slot"],
        CodeSample(["ElementsMetadataDemo.cs"], Result: ElementsMetadataDemo())
    ];
}
