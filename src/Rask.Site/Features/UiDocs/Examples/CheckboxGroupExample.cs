namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxGroupExample : Component
{
    private ICollection<string> Notifications { get; set; } = ["push", "email"];

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.CheckboxGroup.Bind(() => Notifications).Live().Label("Notifications")[
                Ui.Checkbox.Value("push").Label("Push notifications"),
                Ui.Checkbox.Value("email").Label("Email"),
                Ui.Checkbox.Value("app").Label("In-app alerts"),
                Ui.Checkbox.Value("sms").Label("SMS")
            ],
            Ui.Text.Data("testid", "ui-checkbox-group-state")[$"Notify by {string.Join(", ", Notifications)}."]
        ];
}
