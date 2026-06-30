using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Bootstrap section — <see cref="BsCardsDemo" /> (BsCard family).</summary>
[Route("bootstrap/cards")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BsCardsPage : Component
{
    protected override RenderResult Head => Title()["Cards — Bootstrap — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Cards",
            "Compose cards from typed section components — BsCard, BsCardHeader/Body/Footer, "
            + "BsCardTitle/Subtitle/Text/Image."),
        CodeSample(["BsCardsDemo.cs"], Result: BsCardsDemo())
    ];
}
