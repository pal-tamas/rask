using System.Globalization;

namespace Rask.Site.Features;

public sealed partial class SvgClickableDemo : Component
{
    private static readonly (string Name, string Hex)[] Swatches =
    [
        ("Violet", "#7C3AED"),
        ("Indigo", "#512BD4"),
        ("Teal", "#0D9488"),
        ("Amber", "#D97706")
    ];

    private int _selected;

    protected override Component? Render() =>
        [
            Svg.Width("240").Height("48").ViewBox("0 0 240 48")[Swatches.Select(Swatch)],
            P.Class("mt-2 mb-0 text-sm text-ui-muted")[
                "Selected colour: ",
                Strong[Swatches[_selected].Name]
            ]
        ];

    // Keyed so the diff codec reconciles the swatches by identity rather than by position.
    private Component Swatch((string Name, string Hex) swatch, int index) =>
        Circle
            .Cx((24 + (index * 56)).ToString(CultureInfo.InvariantCulture))
            .Cy("24")
            .R("18")
            .Fill(index == _selected ? swatch.Hex : "#e5e7eb")
            .Stroke("#1f2937")
            .StrokeWidth("2")
            .PointerEvents("all")
            .Style("cursor: pointer;")
            .OnClick(() => _selected = index)
            .Key(swatch.Hex);
}
