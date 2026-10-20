namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TabsIconsExample : Component
{
    protected override Component? Render() =>
        Ui.Tabs[
            Ui.Tab.Name("profile").Icon(Ui.IconName.User)["Profile"],
            Ui.Tab.Name("account").Icon(Ui.IconName.Cog6Tooth)["Account"],
            Ui.Tab.Name("billing").Icon(Ui.IconName.Banknotes)["Billing"]
        ];
}
