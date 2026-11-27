namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemTeamExample : Component
{
    private string _ezra = "Admin";
    private string _noor = "Member";

    protected override Component? Render() =>
        Ui.ItemGroup.Class("max-w-xl")[
            Ui.Item[
                Ui.ItemMedia[Ui.Avatar.Sm.Circle().Src("/img/avatars/amber.svg")],
                Ui.ItemHeading["Mara Lind ", Ui.Text.Inline().Subtle["(you)"]],
                Ui.ItemSubheading["mara@example.com"],
                Ui.ItemActions[Ui.Text["Owner"]]
            ],
            Ui.Item[
                Ui.ItemMedia[Ui.Avatar.Sm.Circle().Src("/img/avatars/sky.svg")],
                Ui.ItemHeading["Ezra Cole"],
                Ui.ItemSubheading["ezra@example.com"],
                Ui.ItemActions[Role(_ezra, role => { _ezra = role; })]
            ],
            Ui.Item[
                Ui.ItemMedia[Ui.Avatar.Sm.Circle().Src("/img/avatars/emerald.svg")],
                Ui.ItemHeading["Noor Haddad"],
                Ui.ItemSubheading["noor@example.com"],
                Ui.ItemActions[Role(_noor, role => { _noor = role; })]
            ],
            Ui.Item[
                Ui.ItemMedia[Ui.Avatar.Sm.Circle().Icon(Ui.IconName.Envelope)],
                Ui.ItemHeading["mia@example.com"],
                Ui.ItemSubheading["Invited 2 days ago"],
                Ui.ItemActions[
                    Ui.Dropdown.End[
                        Ui.Button.Sm.Ghost.Icon(Ui.IconName.EllipsisHorizontal).Attributes(("aria-label", "Invite options")),
                        Ui.Menu[Ui.MenuItem["Resend invite"], Ui.MenuItem.Danger["Revoke invite"]]
                    ]
                ]
            ]
        ];

    private static Component Role(string role, Action<string> choose) =>
        Ui.Dropdown.End[
            Ui.Button.Sm.Ghost.IconTrailing(Ui.IconName.ChevronDown).Inset(Ui.Inset.Right)[role],
            Ui.Menu[
                Ui.MenuRadioGroup.Value(role).OnChange(choose)[
                    Ui.MenuRadio.Value("Admin")["Admin"],
                    Ui.MenuRadio.Value("Member")["Member"]
                ],
                Ui.MenuSeparator,
                Ui.MenuItem.Danger["Remove from team…"]
            ]
        ];
}
