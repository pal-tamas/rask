namespace Rask.Site.Features;

public sealed partial class SvgGradientDemo : Component
{
    protected override Component? Render() =>
        RaskLogo.Size(120).GradientId("svgPageBolt");
}
