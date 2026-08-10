namespace Rask.Example.Shared.Features;

public sealed partial class SvgShapesDemo : Component
{
    protected override Component? Render() =>
        Svg.Width("200").Height("80").ViewBox("0 0 200 80")[
            Rect.X("5").Y("5").Width("60").Height("70").Rx("8").Fill("#7C3AED"),
            Circle.Cx("105").Cy("40").R("35").Fill("#0D9488"),
            Line
                .X1("150")
                .Y1("10")
                .X2("195")
                .Y2("70")
                .Stroke("#D97706")
                .StrokeWidth("6")
                .StrokeLinecap("round")
        ];
}
