namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalloutColorsExample : Component
{
    private static readonly Ui.Color[] Colors =
    [
        Ui.Color.Zinc, Ui.Color.Red, Ui.Color.Orange, Ui.Color.Amber, Ui.Color.Yellow, Ui.Color.Lime,
        Ui.Color.Green, Ui.Color.Emerald, Ui.Color.Teal, Ui.Color.Cyan, Ui.Color.Sky, Ui.Color.Blue,
        Ui.Color.Indigo, Ui.Color.Violet, Ui.Color.Purple, Ui.Color.Fuchsia, Ui.Color.Pink, Ui.Color.Rose,
    ];

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Colors.Select(color => Ui.Callout.Key(color).Color(color).Icon(Ui.IconName.ExclamationCircle).Inline()
                .Heading("You've received a new message.")
                .Actions(Ui.Button["View message"]))
        ];
}
