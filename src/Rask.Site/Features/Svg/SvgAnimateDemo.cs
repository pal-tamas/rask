namespace Rask.Site.Features;

// SVG's own animation elements, straight from MDN's data: the browser runs them, so nothing re-renders.
public sealed partial class SvgAnimateDemo : Component
{
    protected override Component? Render() =>
        Svg.Width("200").Height("80").ViewBox("0 0 200 80")[
            Circle.Cx("40").Cy("40").R("20").Fill("#7C3AED")[
                Animate.AttributeName("r").Values("20;32;20").Dur("2s").RepeatCount("indefinite")
            ],
            Rect.X("120").Y("20").Width("40").Height("40").Rx("6").Fill("#0D9488")[
                AnimateTransform
                    .AttributeName("transform")
                    .Type("rotate")
                    .From("0 140 40")
                    .To("360 140 40")
                    .Dur("3s")
                    .RepeatCount("indefinite")
            ]
        ];
}
