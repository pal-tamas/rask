namespace Rask.Example.Shared.Features;

public sealed partial class SvgShapesDemo : Component
{
    protected override Component? Render() =>
        Svg("200", "80", "0 0 200 80")[
            Rect("5", "5", "60", "70", "8", Fill: "#7C3AED"),
            Circle("105", "40", "35", Fill: "#0D9488"),
            Line("150", "10", "195", "70",
                Stroke: "#D97706", StrokeWidth: "6", StrokeLinecap: "round")
        ];
}
