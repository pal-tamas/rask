using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Bootstrap section — <see cref="BsIconsDemo" /> (BsIcon + typed BsIconName).</summary>
[Route("bootstrap/icons")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BsIconsPage : Component
{
    protected override RenderResult Head => Title()["Icons — Bootstrap — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Icons",
            "Bootstrap Icons through the typed BsIconName enum — all 2078 glyphs are compile-checked, "
            + "with no string class names to mistype."),
        CodeSample(["BsIconsDemo.cs"], Result: BsIconsDemo())
    ];
}
