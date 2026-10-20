namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DropdownHeadingsExample : Component
{
    protected override Component? Render() =>
        Ui.Dropdown[
            Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Headings"],
            Ui.Menu[
                Ui.MenuGroup.Heading("Account")[
                    Ui.MenuItem["Profile"],
                    Ui.MenuItem["Permissions"]
                ],
                Ui.MenuGroup.Heading("Billing")[
                    Ui.MenuItem["Transactions"],
                    Ui.MenuItem["Payouts"],
                    Ui.MenuItem["Refunds"]
                ],
                Ui.MenuItem["Logout"]
            ]
        ];
}
