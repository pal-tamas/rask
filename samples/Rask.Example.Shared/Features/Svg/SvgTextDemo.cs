namespace Rask.Example.Shared.Features;

public sealed partial class SvgTextDemo : Component
{
    protected override Component? Render() =>
        Svg("220", "60", "0 0 220 60")[
            SvgText("10", "38", FontFamily: "sans-serif", FontSize: "28",
                FontWeight: "bold", Fill: "#512BD4")[
                "Ra",
                Tspan(Fill: "#0D9488")["sk"]
            ]
        ];
}
