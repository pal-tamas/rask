namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ItemExample : Component
{
    private bool _twoFactor = true;
    private string _language = "English";

    protected override Component? Render() =>
        Div.Class("max-w-xl space-y-6")[
            Ui.Item[
                Ui.ItemMedia[Ui.Avatar.Circle().Src("/img/avatars/amber.svg")],
                Ui.ItemHeading["Mara Lind"],
                Ui.ItemSubheading["mara@example.com"],
                Ui.ItemActions[Ui.Button.Sm.IconTrailing(Ui.IconName.ArrowUpRight)["Edit profile"]]
            ],
            Ui.ItemGroup[
                Ui.Item[
                    Ui.ItemHeading["Two-factor authentication"],
                    Ui.ItemSubheading["Ask for a code when you sign in on a new device."],
                    Ui.ItemActions[
                        Ui.Switch.Value(_twoFactor).OnChange(on => { _twoFactor = on; })
                            .Attributes(("aria-label", "Two-factor authentication"))
                    ]
                ],
                Ui.Item[
                    Ui.ItemHeading["Language"],
                    Ui.ItemActions[
                        Ui.Dropdown.End[
                            Ui.Button.Sm.Ghost.IconTrailing(Ui.IconName.ChevronDown).Inset(Ui.Inset.Right)[_language],
                            Ui.Menu[
                                Ui.MenuRadioGroup.Value(_language).OnChange(language => { _language = language; })[
                                    Ui.MenuRadio.Value("English")["English"],
                                    Ui.MenuRadio.Value("Español")["Español"],
                                    Ui.MenuRadio.Value("Français")["Français"]
                                ]
                            ]
                        ]
                    ]
                ],
                Ui.Item[
                    Ui.ItemHeading["Sign out everywhere else"],
                    Ui.ItemSubheading["You're signed in on 3 other devices."],
                    Ui.ItemActions[Ui.Button.Sm["Sign out"]]
                ]
            ]
        ];
}
