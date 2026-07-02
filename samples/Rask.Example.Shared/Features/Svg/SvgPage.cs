using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

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

    // The interactive selection now lives in SvgClickableDemo; on the page itself the swatch
    // index is fixed, so this just names the first colour in the prose note below.
    private const int _selected = 0;

    protected override Component? Head => Title()["SVG — Rask"];

    protected override Component? Render() =>
    [
        PageHeader.Render(
            "SVG",
            "SVG elements are first-class core components. svg, g, path, the shapes, text, gradients and filters all have typed factories that flow through scoped CSS, keyed lists, and event handlers — no Raw() required."),

        H2(Class: "h4 mt-4 mb-3")["Shapes inside an <svg>"],
        CodeSample(
            ["SvgShapesDemo.cs"],
            Notes:
            "Presentation attributes (Fill, Stroke, StrokeWidth, StrokeLinecap, …) live on the shared SvgElement base, so every shape exposes them as optional factory parameters.",
            Result: SvgShapesDemo()),

        H2(Class: "h4 mt-5 mb-3")["Gradients via <defs> and <linearGradient>"],
        CodeSample(
            ["SvgGradientDemo.cs"],
            Notes:
            "The Rask brand mark itself is built this way — see Demos/RaskLogo.cs. A nested SvgTitle gives the graphic its accessible name.",
            Result: SvgGradientDemo()),

        H2(Class: "h4 mt-5 mb-3")["Clickable shapes — OnClick on any element"],
        CodeSample(
            ["SvgClickableDemo.cs"],
            Notes:
            $"Click a swatch — the selection re-renders live over the same transport as the rest of the page. Selected: {Swatches[_selected].Name}.",
            Result: SvgClickableDemo()),

        H2(Class: "h4 mt-5 mb-3")["Text with <text> and <tspan>"],
        CodeSample(
            ["SvgTextDemo.cs"],
            Notes:
            "SvgText is the <text> tag (renamed to avoid colliding with the Text primitive); Tspan styles a run inside it.",
            Result: SvgTextDemo())
    ];
}
