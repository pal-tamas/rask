namespace Rask.Example.Shared.Features;

public sealed partial class SvgTextDemo : Component
{
    protected override Component? Render() =>
        Svg.Width("220").Height("60").ViewBox("0 0 220 60")[
            SvgText
                .X("10")
                .Y("38")
                .FontFamily("sans-serif")
                .FontSize("28")
                .FontWeight("bold")
                .Fill("#512BD4")[
                "Ra",
                Tspan.Fill("#0D9488")["sk"]
            ]
        ];
}
