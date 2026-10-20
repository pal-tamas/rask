namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TabsScrollableExample : Component
{
    private static readonly string[] Sections =
        ["Profile", "Account", "Billing", "Security", "Notifications", "Integrations", "API"];

    protected override Component? Render() =>
        Div.Class("max-w-sm")[
            Ui.Tabs.Scrollable()[Sections.Select(name => Ui.Tab.Key(name).Name(name.ToLowerInvariant())[name])]
        ];
}
