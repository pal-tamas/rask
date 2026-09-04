namespace Rask.Example.Shared.Features;

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
            Svg.Width("240").Height("48").ViewBox("0 0 240 48")[BuildSwatches()],
            P.Class("mt-2 mb-0 text-sm text-ui-muted")[
                "Selected colour: ",
                Strong[Swatches[_selected].Name]
            ]
        ];

    // Keyed so the diff codec reconciles the swatches by identity rather than by position.
    private List<Component> BuildSwatches()
    {
        var children = new List<Component>();
        for (var i = 0; i < Swatches.Length; i++)
        {
            var index = i;
            var (_, hex) = Swatches[i];
            children.Add(Circle
                .Cx((24 + (i * 56)).ToString())
                .Cy("24")
                .R("18")
                .Fill(i == _selected ? hex : "#e5e7eb")
                .Stroke("#1f2937")
                .StrokeWidth("2")
                .PointerEvents("all")
                .Style("cursor: pointer;")
                .OnClick(() => _selected = index)
                .Key(hex));
        }

        return children;
    }
}
