namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SwitchLeftExample : Component
{
    protected override Component? Render() =>
        Ui.Fieldset[
            Ui.Legend["Email notifications"],
            Div.Class("space-y-3")[
                Ui.Switch.Value(false).Label("Communication emails").Left,
                Ui.Switch.Value(false).Label("Marketing emails").Left,
                Ui.Switch.Value(false).Label("Social emails").Left,
                Ui.Switch.Value(false).Label("Security emails").Left
            ]
        ];
}
