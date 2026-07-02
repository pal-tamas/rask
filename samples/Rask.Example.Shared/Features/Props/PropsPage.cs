using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("props")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class PropsPage : Component
{
    protected override Component? Head => Title()["Universal props — Rask"];

    protected override Component? Render() =>
    [
        PageHeader.Render(
            "Universal props",
            "Every tag accepts Id, Class, Style, Data, and the accessibility props Role, TabIndex, and Aria. "
            + "They render in that exact order, ahead of any tag-specific attributes."),
        H2(Class: "h4 mt-4 mb-3")["Id, Class, Style"],
        CodeSample(
            ["PropsIdClassStyleDemo.cs"],
            Result: PropsIdClassStyleDemo()),
        H2(Class: "h4 mt-5 mb-3")["Data — dictionary expands as data-*"],
        CodeSample(
            ["PropsDataDemo.cs"],
            Notes:
            "Null values render as bare attributes (e.g. data-new). That's also how boolean attrs like disabled work elsewhere.",
            Result: PropsDataDemo()),
        H2(Class: "h4 mt-5 mb-3")["Aria, Role, TabIndex — accessibility"],
        CodeSample(
            ["PropsAriaDemo.cs"],
            Notes:
            "Aria is the data-* model applied to ARIA: each entry expands to aria-{key} (value HTML-encoded, "
            + "null → bare attribute). Role and TabIndex are typed because they aren't aria-* attributes. "
            + "See the accessibility guide and the RASK023 Img-alt analyzer.",
            Result: PropsAriaDemo()),
        H2(Class: "h4 mt-5 mb-3")["Attribute order"],
        CodeSample(
            ["PropsAttributeOrderDemo.cs"],
            Notes:
            "Render order is base props first (id, class, style, data-*, role, tabindex, aria-*), then tag-specific. "
            + "Tests enforce it. Predictable for diffing and DOM tooling.",
            Result: PropsAttributeOrderDemo())
    ];
}
