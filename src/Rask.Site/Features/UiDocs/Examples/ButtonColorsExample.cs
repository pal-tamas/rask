namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ButtonColorsExample : Component
{
    private static readonly Ui.Color[] Hues =
    [Ui.Color.Blue, Ui.Color.Red];

    protected override Component? Render() =>
        Div.Class("flex flex-col gap-3")[
            Hues.Select(hue => Div.Key(hue).Class("flex flex-wrap gap-3")[
                Ui.Button.Primary.Color(hue)["Primary"],
                Ui.Button.Filled.Color(hue)["Filled"],
                Ui.Button.Color(hue)["Outline"],
                Ui.Button.Ghost.Color(hue)["Ghost"],
                Ui.Button.Subtle.Color(hue)["Subtle"]
            ])
        ];
}
