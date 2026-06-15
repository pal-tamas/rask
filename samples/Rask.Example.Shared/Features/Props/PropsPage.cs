using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("props")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class PropsPage : Component
{
    protected override RenderResult Head => Title()["Universal props — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Universal props",
            "Every tag accepts Id, Class, Style, and Data. They render in that exact order, ahead of any tag-specific attributes."),
        H2(Class: "h4 mt-4 mb-3")["Id, Class, Style"],
        CodeSample(
            EmbeddedSource.Read("PropsIdClassStyleDemo.cs"),
            Result: PropsIdClassStyleDemo()),
        H2(Class: "h4 mt-5 mb-3")["Data — dictionary expands as data-*"],
        CodeSample(
            EmbeddedSource.Read("PropsDataDemo.cs"),
            Notes:
            "Null values render as bare attributes (e.g. data-new). That's also how boolean attrs like disabled work elsewhere.",
            Result: PropsDataDemo()),
        H2(Class: "h4 mt-5 mb-3")["Attribute order"],
        CodeSample(
            EmbeddedSource.Read("PropsAttributeOrderDemo.cs"),
            Notes:
            "Render order is base props first (id, class, style, data-*), then tag-specific. Tests enforce it. Predictable for diffing and DOM tooling.",
            Result: PropsAttributeOrderDemo())
    ];
}
