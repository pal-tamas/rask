namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToastPlacementExample : Component
{
    private static readonly (Layout Layout, string Label)[] Layouts =
    [
        (Layout.Default, "Ui.Toast"),
        (Layout.Inverted, "Ui.Toast.Invert()"),
        (Layout.TopEnd, "Ui.Toast.TopEnd"),
        (Layout.Stack, "Ui.ToastGroup[Ui.Toast]"),
        (Layout.Expanded, "Ui.ToastGroup.Expanded()[Ui.Toast]"),
    ];

    private Layout _layout;

    private enum Layout
    {
        Default,
        Inverted,
        TopEnd,
        Stack,
        Expanded,
    }

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-2")[
            Layouts.Select(Choice),
            // What an app writes once, in its layout. Every toast raised on this page shows in it.
            _layout switch
            {
                Layout.Inverted => Ui.Toast.Invert(),
                Layout.TopEnd => Ui.Toast.TopEnd,
                Layout.Stack => Ui.ToastGroup[Ui.Toast],
                Layout.Expanded => Ui.ToastGroup.Expanded()[Ui.Toast],
                _ => Ui.Toast,
            }
        ];

    private Component Choice((Layout Layout, string Label) choice)
    {
        var button = Ui.Button.Key(choice.Label).Sm.Id("toast-layout-" + choice.Layout.ToString().ToLowerInvariant())
            .OnClick(() => _layout = choice.Layout);
        return (choice.Layout == _layout ? button.Primary : button.Outline)[choice.Label];
    }
}
