namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SwitchExample : Component
{
    private bool _notifications;

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.Field.Variant(Ui.FieldVariant.Inline)[
                Ui.Label["Enable notifications"],
                Ui.Switch.Value(_notifications).OnChange(value => { _notifications = value; }).Id("sw-notify"),
                Ui.Error
            ],
            Ui.Text.Data("testid", "ui-switch-state")[$"Notifications {(_notifications ? "on" : "off")}."]
        ];
}
