namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TabsPaddedExample : Component
{
    protected override Component? Render() =>
        Ui.Tabs.Class("px-4")[
            Ui.Tab.Name("profile")["Profile"],
            Ui.Tab.Name("account")["Account"],
            Ui.Tab.Name("billing")["Billing"]
        ];
}
