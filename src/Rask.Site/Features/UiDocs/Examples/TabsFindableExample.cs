namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TabsFindableExample : Component
{
    protected override Component? Render() =>
        Ui.TabGroup.Findable()[
            Ui.Tabs[
                Ui.Tab.Name("profile").Selected()["Profile"],
                Ui.Tab.Name("account")["Account"],
                Ui.Tab.Name("billing")["Billing"]
            ],
            Ui.TabPanel.Name("profile").Class("px-2 pt-6! text-sm")["Manage your public profile and personal information."],
            Ui.TabPanel.Name("account").Class("px-2 pt-6! text-sm")["Update your email address, password, and security settings."],
            Ui.TabPanel.Name("billing").Class("px-2 pt-6! text-sm")["Invoice INV-1042 is due on September 1."]
        ];
}
