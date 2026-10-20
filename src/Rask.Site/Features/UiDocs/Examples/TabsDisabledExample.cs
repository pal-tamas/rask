namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TabsDisabledExample : Component
{
    protected override Component? Render() =>
        Ui.Tabs[
            Ui.Tab.Name("open")["Open", Ui.Badge.Sm["12"]],
            Ui.Tab.Name("archived").Disabled()["Archived"],
            Ui.Tab.Name("failed")["Failed", Ui.Badge.Sm.Solid.Color(Ui.Color.Red)["3"]]
        ];
}
