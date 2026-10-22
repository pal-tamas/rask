namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SwitchFieldsetExample : Component
{
    private bool _communication = true;
    private bool _marketing;
    private bool _social;
    private bool _security = true;

    protected override Component? Render() =>
        Ui.Fieldset.Class("max-w-md")[
            Ui.Legend["Email notifications"],
            Div.Class("space-y-4")[
                Ui.Switch.Value(_communication).OnChange(on => { _communication = on; }).Label("Communication emails")
                    .Description("Receive emails about your account activity."),
                Ui.Separator.Subtle,
                Ui.Switch.Value(_marketing).OnChange(on => { _marketing = on; }).Label("Marketing emails")
                    .Description("Receive emails about new products, features, and more."),
                Ui.Separator.Subtle,
                Ui.Switch.Value(_social).OnChange(on => { _social = on; }).Label("Social emails")
                    .Description("Receive emails for friend requests, follows, and more."),
                Ui.Separator.Subtle,
                Ui.Switch.Value(_security).OnChange(on => { _security = on; }).Label("Security emails")
                    .Description("Receive emails about your account activity and security.")
            ]
        ];
}
