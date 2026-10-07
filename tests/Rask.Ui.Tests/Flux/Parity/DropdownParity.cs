using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/dropdown</c>, example for example.
/// </summary>
/// <remarks>
///     <para>
///     As the page loads every menu is closed, so <c>scripts/flux/parity.mjs</c> compares the dropdown, its
///     trigger's room and the closed popup. The component is the OPEN menu, and
///     <c>scripts/flux/parity-menu.mjs</c> opens each of these on both pages and compares that.
///     </para>
///     <para>
///     A trigger with words is the real <c>Ui.Button</c>. The icon-only ones and the profile are stand-ins
///     (<c>data-parity-skip</c>): Flux's snippets do not say which icon, the profile is its own page, and
///     all a dropdown needs of them is the room they take, because the menu is placed against it.
///     </para>
/// </remarks>
public sealed partial class DropdownParity : FluxParity
{
    private sealed class Model
    {
        public string Sort { get; set; } = "name";

        public string Activity { get; set; } = "latest";
    }

    private readonly Model _model = new();

    public override string Page => "dropdown";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Example(Ui.Dropdown[
            Trigger("Options"),
            Ui.Menu[
                Ui.MenuItem.Icon(Ui.IconName.Plus)["New post"],
                Ui.MenuSeparator,
                Ui.MenuSubmenu.Heading("Sort by")[
                    Ui.MenuRadioGroup.Bind(() => _model.Sort)[
                        Ui.MenuRadio.Value("name")["Name"],
                        Ui.MenuRadio.Value("date")["Date"],
                        Ui.MenuRadio.Value("popularity")["Popularity"]
                    ]
                ],
                Ui.MenuSubmenu.Heading("Filter")[
                    Ui.MenuCheckbox.Value(true)["Draft"],
                    Ui.MenuCheckbox.Value(true)["Published"],
                    Ui.MenuCheckbox.Value(false)["Archived"]
                ],
                Ui.MenuSeparator,
                Ui.MenuItem.Danger.Icon(Ui.IconName.Trash)["Delete"]
            ]
        ]));

        yield return ("navigation-menus", Example(Ui.Dropdown.Bottom.End[
            Room("width:172.14px;height:40px"),
            Ui.Navmenu[
                Ui.NavmenuItem.Href("#").Icon(Ui.IconName.User)["Account"],
                Ui.NavmenuItem.Href("#").Icon(Ui.IconName.BuildingStorefront)["Profile"],
                Ui.NavmenuItem.Href("#").Icon(Ui.IconName.CreditCard)["Billing"],
                Ui.NavmenuItem.Href("#").Icon(Ui.IconName.ArrowRightStartOnRectangle)["Logout"],
                Ui.NavmenuItem.Href("#").Icon(Ui.IconName.Trash).Danger["Delete"]
            ]
        ]));

        yield return ("positioning", Example(Ui.Dropdown.Top.Start[Room("width:40px;height:40px"), Links()]));

        yield return ("offset-&-gap", Example(Ui.Dropdown.Offset(-15).Gap(2)[Room("width:40px;height:40px"), Links()]));

        yield return ("keyboard-hints", Example(Ui.Dropdown[
            Trigger("Options"),
            Ui.Menu[
                Ui.MenuItem.Icon(Ui.IconName.PencilSquare).Kbd("⌘S")["Save"],
                Ui.MenuItem.Icon(Ui.IconName.DocumentDuplicate).Kbd("⌘D")["Duplicate"],
                Ui.MenuItem.Icon(Ui.IconName.Trash).Danger.Kbd("⌘⌫")["Delete"]
            ]
        ]));

        yield return ("checkbox-items", Example(Ui.Dropdown[
            Trigger("Permissions"),
            Ui.Menu[
                Ui.MenuCheckbox.Value(true)["Read"],
                Ui.MenuCheckbox.Value(true)["Write"],
                Ui.MenuCheckbox.Value(false)["Delete"]
            ]
        ]));

        yield return ("radio-items", Example(Ui.Dropdown[
            Trigger("Sort by"),
            Ui.Menu[
                Ui.MenuRadioGroup.Bind(() => _model.Activity)[
                    Ui.MenuRadio.Value("latest")["Latest activity"],
                    Ui.MenuRadio.Value("created")["Date created"],
                    Ui.MenuRadio.Value("popular")["Most popular"]
                ]
            ]
        ]));

        yield return ("groups", Example(Ui.Dropdown[
            Trigger("Options"),
            Ui.Menu[
                Ui.MenuItem["View"],
                Ui.MenuItem["Transfer"],
                Ui.MenuSeparator,
                Ui.MenuItem["Publish"],
                Ui.MenuItem["Share"],
                Ui.MenuSeparator,
                Ui.MenuItem.Danger["Delete"]
            ]
        ]));

        yield return ("groups-with-headings", Example(Ui.Dropdown[
            Trigger("Options"),
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
        ]));

        // Flux's snippet writes the radios straight into the submenu; what its page renders has them in a group.
        yield return ("submenus", Example(Ui.Dropdown[
            Trigger("Options"),
            Ui.Menu[
                Ui.MenuSubmenu.Heading("Sort by")[
                    Ui.MenuRadioGroup.Bind(() => _model.Sort)[
                        Ui.MenuRadio.Value("name")["Name"],
                        Ui.MenuRadio.Value("date")["Date"],
                        Ui.MenuRadio.Value("popularity")["Popularity"]
                    ]
                ],
                Ui.MenuSubmenu.Heading("Filter")[
                    Ui.MenuCheckbox.Value(true)["Draft"],
                    Ui.MenuCheckbox.Value(true)["Published"],
                    Ui.MenuCheckbox.Value(false)["Archived"]
                ],
                Ui.MenuSeparator,
                Ui.MenuItem.Danger["Delete"]
            ]
        ]));

        yield return ("keep-open", Example(Ui.Dropdown[
            Trigger("Filter"),
            Ui.Menu.KeepOpen(true)[
                Ui.MenuCheckbox.Value(true)["Draft"],
                Ui.MenuCheckbox.Value(true)["Published"],
                Ui.MenuCheckbox.Value(false)["Archived"]
            ]
        ]));

        yield return ("keep-open", Example(Ui.Dropdown[
            Trigger("Filters"),
            Ui.Menu[
                Ui.MenuCheckbox.Value(true).KeepOpen(true)["Draft"],
                Ui.MenuCheckbox.Value(true).KeepOpen(true)["Published"],
                Ui.MenuCheckbox.Value(false).KeepOpen(true)["Archived"],
                Ui.MenuSeparator,
                Ui.MenuItem.Danger["Clear"]
            ]
        ]));
    }

    // The navigation menu Flux's two placement examples open.
    private static Component Links() =>
        Ui.Navmenu[
            Ui.NavmenuItem.Href("#")["Account"],
            Ui.NavmenuItem.Href("#")["Profile"],
            Ui.NavmenuItem.Href("#")["Billing"],
            Ui.NavmenuItem.Href("#")["Logout"]
        ];

    // Centred in a block of its own, as each example is on Flux's page.
    private static Component Example(Component dropdown) => Row(Div[dropdown]);

    /// <summary>Flux's <c>&lt;flux:button icon:trailing="chevron-down"&gt;</c>.</summary>
    internal static Component Trigger(string label) => Ui.Button.IconTrailing(Ui.IconName.ChevronDown)[label];

    /// <summary>A trigger that only has to be a box of Flux's size: its icon button, its profile.</summary>
    internal static HTMLButtonElement Room(string style) =>
        Button.Type(ButtonType.Button).Style(style).Data("parity-skip", "");
}
