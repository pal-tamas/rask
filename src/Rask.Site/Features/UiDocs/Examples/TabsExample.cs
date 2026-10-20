namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TabsExample : Component
{
    private string _settings = "profile";

    protected override Component? Render() =>
        Div[
            Ui.TabGroup[
                Ui.Tabs.Value(_settings).OnChange(tab => _settings = tab)[
                    Ui.Tab.Name("profile")["Profile"],
                    Ui.Tab.Name("account")["Account"],
                    Ui.Tab.Name("billing")["Billing"]
                ],
                Ui.TabPanel.Name("profile").Class("pt-4! text-sm")["Your name, your photo and what others see."],
                Ui.TabPanel.Name("account").Class("pt-4! text-sm")["Your email address and how you sign in."],
                Ui.TabPanel.Name("billing").Class("pt-4! text-sm")["Your plan and your invoices."]
            ],
            Ui.Text.Class("mt-2").Data("testid", "ui-tabs-state")[$"Selected: {_settings}."]
        ];
}
