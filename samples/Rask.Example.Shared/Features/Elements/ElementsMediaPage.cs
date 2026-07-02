using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("elements/media")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ElementsMediaPage : Component
{
    protected override Component? Head => Title()["Media & embedded elements — Rask"];

    protected override Component? Render() =>
    [
        PageHeader.Render(
            "Media & embedded elements",
            "Images, media, and embedded content: img, picture/source, audio, video/track, canvas, iframe, "
            + "embed, object, map/area. The media events (OnPlay/OnTimeUpdate/…) live on Audio/Video."),
        H2(Class: "h4 mt-4 mb-3")["Live"],
        CodeSample(["ElementsMediaDemo.cs"], Result: ElementsMediaDemo())
    ];
}
