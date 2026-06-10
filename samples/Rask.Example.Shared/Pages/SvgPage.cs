using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("svg")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class SvgPage : Component
{
    private static readonly (string Name, string Hex)[] Swatches =
    [
        ("Violet", "#7C3AED"),
        ("Indigo", "#512BD4"),
        ("Teal", "#0D9488"),
        ("Amber", "#D97706")
    ];

    private int _selected;

    protected override RenderResult Head => Title()["SVG — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "SVG",
            "SVG elements are first-class core components. svg, g, path, the shapes, text, gradients and filters all have typed factories that flow through scoped CSS, keyed lists, and event handlers — no Raw() required."),

        H2(Class: "h4 mt-4 mb-3")["Shapes inside an <svg>"],
        CodeSample(
            """
            Svg(Width: "200", Height: "80", ViewBox: "0 0 200 80")[
                Rect(X: "5", Y: "5", Width: "60", Height: "70", Rx: "8",
                    Fill: "#7C3AED"),
                Circle(Cx: "105", Cy: "40", R: "35", Fill: "#0D9488"),
                Line(X1: "150", Y1: "10", X2: "195", Y2: "70",
                    Stroke: "#D97706", StrokeWidth: "6", StrokeLinecap: "round")
            ]
            """,
            Notes:
            "Presentation attributes (Fill, Stroke, StrokeWidth, StrokeLinecap, …) live on the shared SvgElement base, so every shape exposes them as optional factory parameters.",
            Result: Svg("200", "80", "0 0 200 80")[
                Rect("5", "5", "60", "70", "8", Fill: "#7C3AED"),
                Circle("105", "40", "35", Fill: "#0D9488"),
                Line("150", "10", "195", "70",
                    Stroke: "#D97706", StrokeWidth: "6", StrokeLinecap: "round")
            ]),

        H2(Class: "h4 mt-5 mb-3")["Gradients via <defs> and <linearGradient>"],
        CodeSample(
            """
            Svg(Width: "120", Height: "120", ViewBox: "22 6 80 108")[
                SvgTitle()["Rask"],
                Defs()[
                    LinearGradient(Id: "bolt", X1: "0", Y1: "0", X2: "1", Y2: "1")[
                        Stop(Offset: "0%", StopColor: "#7C3AED"),
                        Stop(Offset: "100%", StopColor: "#512BD4")
                    ]
                ],
                SvgPath(D: "M72 14 L30 64 L54 64 L48 106 L94 54 L68 54 Z",
                    Fill: "url(#bolt)")
            ]
            """,
            Notes:
            "The Rask brand mark itself is built this way — see Demos/RaskLogo.cs. A nested SvgTitle gives the graphic its accessible name.",
            Result: RaskLogo.Mark(120, "svgPageBolt")),

        H2(Class: "h4 mt-5 mb-3")["Clickable shapes — OnClick on any element"],
        CodeSample(
            """
            // SvgElement carries OnClick/OnClickAsync, so a shape dispatches
            // through the same handler path as a Button.
            Circle(Cx: "20", Cy: "20", R: "18",
                Fill: i == _selected ? hex : "#e5e7eb",
                Stroke: "#1f2937", StrokeWidth: "2",
                OnClick: () => _selected = i)
            """,
            Notes:
            $"Click a swatch — the selection re-renders live over the same transport as the rest of the page. Selected: {Swatches[_selected].Name}.",
            Result: Fragment()[
                Svg("240", "48", "0 0 240 48")[BuildSwatches()],
                P(Class: "mt-2 mb-0 small text-secondary")[
                    "Selected colour: ",
                    Strong()[Swatches[_selected].Name]
                ]
            ]),

        H2(Class: "h4 mt-5 mb-3")["Text with <text> and <tspan>"],
        CodeSample(
            """
            Svg(Width: "220", Height: "60", ViewBox: "0 0 220 60")[
                SvgText(X: "10", Y: "38", FontFamily: "sans-serif",
                    FontSize: "28", FontWeight: "bold", Fill: "#512BD4")[
                    "Ra",
                    Tspan(Fill: "#0D9488")["sk"]
                ]
            ]
            """,
            Notes:
            "SvgText is the <text> tag (renamed to avoid colliding with the Text primitive); Tspan styles a run inside it.",
            Result: Svg("220", "60", "0 0 220 60")[
                SvgText("10", "38", FontFamily: "sans-serif", FontSize: "28",
                    FontWeight: "bold", Fill: "#512BD4")[
                    "Ra",
                    Tspan(Fill: "#0D9488")["sk"]
                ]
            ])
    ];

    // Keyed so the diff codec reconciles the swatches by identity rather than by position.
    private List<Child> BuildSwatches()
    {
        var children = new List<Child>();
        for (var i = 0; i < Swatches.Length; i++)
        {
            var index = i;
            var (_, hex) = Swatches[i];
            children.Add(Circle(
                (24 + (i * 56)).ToString(),
                "24",
                "18",
                Fill: i == _selected ? hex : "#e5e7eb",
                Stroke: "#1f2937",
                StrokeWidth: "2",
                PointerEvents: "all",
                Style: "cursor: pointer;",
                OnClick: () => _selected = index,
                Key: hex));
        }

        return children;
    }
}
