namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BadgeColorsExample : Component
{
    private static readonly Ui.Color?[] Colors =
    [
        null, Ui.Color.Red, Ui.Color.Orange, Ui.Color.Amber, Ui.Color.Yellow, Ui.Color.Lime, Ui.Color.Green,
        Ui.Color.Emerald, Ui.Color.Teal, Ui.Color.Cyan, Ui.Color.Sky, Ui.Color.Blue, Ui.Color.Indigo,
        Ui.Color.Violet, Ui.Color.Purple, Ui.Color.Fuchsia, Ui.Color.Pink, Ui.Color.Rose,
    ];

    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-2")[
            Colors.Select(color => Ui.Badge.Key(color?.ToString() ?? "Zinc").Color(color)[color?.ToString() ?? "Zinc"])
        ];
}
