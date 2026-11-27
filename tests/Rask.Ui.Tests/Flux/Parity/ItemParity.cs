using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/item</c>, example for example.
/// </summary>
/// <remarks>
///     <para>
///     Everything inside the items is the real kit: <c>Ui.Avatar</c>, <c>Ui.Switch</c>, <c>Ui.Button</c>,
///     <c>Ui.Dropdown</c> with its closed <c>Ui.Menu</c>, <c>Ui.Text</c>, <c>Ui.Heading</c> and <c>Ui.Badge</c>.
///     </para>
///     <para>
///     The docs page holds each example in a column 576px wide at most (320px for "Narrow widths", which is how that
///     example shows an item under 24rem), and stacks the lone item over the group with Tailwind's <c>space-y-6</c>:
///     the page's, written here as an app would.
///     </para>
/// </remarks>
public sealed partial class ItemParity : FluxParity
{
    private const string Caleb = "https://unavatar.io/x/calebporzio";

    private const string Josh = "https://unavatar.io/github/joshhanley";

    private const string Hugo = "https://unavatar.io/github/hugosaintemarie";

    public override string Page => "item";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Frame(576,
            Profile(Ui.Avatar.Circle().Src(Caleb)),
            Ui.ItemGroup[
                Ui.Item[
                    Ui.ItemHeading["Two-factor authentication"],
                    Ui.ItemSubheading["Ask for a code when you sign in on a new device."],
                    Ui.ItemActions[Switch("Two-factor authentication", true)]
                ],
                Ui.Item[
                    Ui.ItemHeading["Language"],
                    Ui.ItemActions[Language()]
                ],
                Ui.Item[
                    Ui.ItemHeading["Sign out everywhere else"],
                    Ui.ItemSubheading["You're signed in on 3 other devices."],
                    Ui.ItemActions[Ui.Button.Sm["Sign out"]]
                ]
            ]));

        yield return ("single-item", Frame(576,
            Ui.Item[
                Ui.ItemHeading["Sign out"],
                Ui.ItemSubheading["Your boards stay on this device."],
                Ui.ItemActions[Ui.Button.Sm["Sign out"]]
            ]));

        yield return ("without-a-subheading", Frame(576,
            Ui.ItemGroup[
                Ui.Item[Ui.ItemHeading["Subscribed since"], Ui.ItemActions[Ui.Text["October 4, 2026"]]],
                Ui.Item[Ui.ItemHeading["Seats"], Ui.ItemActions[Ui.Text["3 of 5 used"]]],
                Ui.Item[Ui.ItemHeading["Renew automatically"], Ui.ItemActions[Switch("Renew automatically", true)]]
            ]));

        yield return ("actions", Frame(576,
            Ui.ItemGroup[
                ClaudeCode(),
                ErrorReports(),
                Ui.Item[
                    Ui.ItemHeading["Next payment"],
                    Ui.ItemSubheading["December 4, 2026"],
                    Ui.ItemActions[Ui.Heading["$60.00"]]
                ]
            ]));

        yield return ("media", Frame(576,
            Ui.ItemGroup[
                Ui.Item[
                    Ui.ItemMedia[Ui.Avatar.Sm.Circle().Src(Caleb)],
                    Ui.ItemHeading["Caleb Porzio"],
                    Ui.ItemSubheading["caleb@example.com"]
                ],
                Ui.Item[
                    Ui.ItemMedia[Ui.Avatar.Sm.Icon(Ui.IconName.CommandLine)],
                    Ui.ItemHeading["Claude Code"],
                    Ui.ItemSubheading["Version 2.1.4"]
                ],
                Ui.Item[
                    Ui.ItemMedia[Ui.Avatar.Sm.Initials("N")],
                    Ui.ItemHeading["Northwind"],
                    Ui.ItemSubheading["8 boards"]
                ]
            ]));

        yield return ("narrow-widths", Frame(320,
            Profile(Ui.Avatar.Circle().Src(Caleb)),
            Ui.ItemGroup[ClaudeCode(), ErrorReports()]));

        yield return ("examples", Frame(576,
            Ui.ItemGroup[
                Ui.Item[
                    Ui.ItemMedia[Ui.Avatar.Sm.Circle().Src(Caleb)],
                    Ui.ItemHeading["Caleb Porzio ", Ui.Text.Inline().Subtle["(you)"]],
                    Ui.ItemSubheading["caleb@example.com"],
                    Ui.ItemActions[Ui.Text["Owner"]]
                ],
                Member(Josh, "Josh Hanley", "josh@example.com", "Admin"),
                Member(Hugo, "Hugo Sainte-Marie", "hugo@example.com", "Member"),
                Ui.Item[
                    Ui.ItemMedia[Ui.Avatar.Sm.Circle().Icon(Ui.IconName.Envelope)],
                    Ui.ItemHeading["mia@example.com"],
                    Ui.ItemSubheading["Invited 2 days ago"],
                    Ui.ItemActions[
                        Ui.Dropdown.End[
                            Options("Invite options"),
                            Ui.Menu[Ui.MenuItem["Resend invite"], Ui.MenuItem.Danger["Revoke invite"]]
                        ]
                    ]
                ]
            ]));

        yield return ("examples", Frame(576,
            Ui.ItemGroup[
                App("Grok Bot", "Used 4 minutes ago"),
                App("Amp", "Used yesterday"),
                App("Cursor", "Never used")
            ]));

        yield return ("examples", Frame(576,
            Ui.ItemGroup[
                Ui.Item[
                    Ui.ItemHeading["Pro plan"],
                    Ui.ItemSubheading["$20 a month for each person"],
                    Ui.ItemActions[Ui.Badge.Sm.Green["Active"]]
                ],
                Ui.Item[
                    Ui.ItemHeading["Next payment"],
                    Ui.ItemSubheading["December 4, 2026"],
                    Ui.ItemActions[Ui.Heading["$60.00"]]
                ],
                Ui.Item[
                    Ui.ItemHeading["Payment details and invoices"],
                    Ui.ItemSubheading["Managed by Stripe."],
                    Ui.ItemActions[Ui.Button.Sm.IconTrailing(Ui.IconName.ArrowUpRight)["Manage billing"]]
                ]
            ]));

        yield return ("examples", Frame(576,
            Ui.ItemGroup[
                Team("P", "Paraframe", "24 boards", "Owner"),
                Team("N", "Northwind", "8 boards", "Member")
            ]));

        yield return ("examples", Frame(576,
            Ui.ItemGroup[
                Notification("Comments", "When someone comments on a board you own.", true),
                Notification("Mentions", "When someone mentions you anywhere.", true),
                Notification("Weekly digest", "A summary of what changed, every Monday.", false)
            ]));

        yield return ("examples", Frame(576,
            Ui.ItemGroup[
                Ui.Item[
                    Ui.ItemHeading["Transfer ownership"],
                    Ui.ItemSubheading["Give this team to someone else. You'll stay on as an admin."],
                    Ui.ItemActions[Ui.Button.Sm["Transfer…"]]
                ],
                Ui.Item[
                    Ui.ItemHeading["Delete team"],
                    Ui.ItemSubheading["Its boards are deleted for everyone. This can't be undone."],
                    Ui.ItemActions[Ui.Button.Sm.Outline.Red["Delete…"]]
                ]
            ]));
    }

    // The docs page's holder: centred, as wide as the column up to the example's own limit, its items 24px apart.
    private static Component Frame(int maxWidth, params Component[] items) =>
        Div.Style("display:flex;justify-content:center")[
            Div.Data("space-y", "6").Style(FormattableString.Invariant($"width:100%;max-width:{maxWidth}px"))[items]
        ];

    private static Component Profile(Component avatar) =>
        Ui.Item[
            Ui.ItemMedia[avatar],
            Ui.ItemHeading["Caleb Porzio"],
            Ui.ItemSubheading["caleb@example.com"],
            Ui.ItemActions[Ui.Button.Sm.IconTrailing(Ui.IconName.ArrowUpRight)["Edit profile"]]
        ];

    private static Component ClaudeCode() =>
        Ui.Item[
            Ui.ItemHeading["Claude Code"],
            Ui.ItemSubheading["Not found on this computer."],
            Ui.ItemActions[Ui.Button.Sm.Subtle["Browse…"], Ui.Button.Sm["Install"]]
        ];

    private static Component ErrorReports() =>
        Ui.Item[
            Ui.ItemHeading["Send error reports"],
            Ui.ItemSubheading["When something goes wrong, we get a report so it can be fixed."],
            Ui.ItemActions[Switch("Send error reports", true)]
        ];

    private static Component Switch(string label, bool on) => Ui.Switch.Value(on).Attributes(("aria-label", label));

    // <flux:dropdown align="end"> around a small ghost button with a chevron, and a menu of radios.
    private static Component Language() =>
        Ui.Dropdown.End[
            Ui.Button.Sm.Ghost.IconTrailing(Ui.IconName.ChevronDown).Inset(Ui.Inset.Right)["English"],
            Ui.Menu[
                Ui.MenuRadioGroup.Value("English")[
                    Ui.MenuRadio.Value("English")["English"],
                    Ui.MenuRadio.Value("Español")["Español"],
                    Ui.MenuRadio.Value("Français")["Français"]
                ]
            ]
        ];

    private static Component Member(string src, string name, string email, string role) =>
        Ui.Item[
            Ui.ItemMedia[Ui.Avatar.Sm.Circle().Src(src)],
            Ui.ItemHeading[name],
            Ui.ItemSubheading[email],
            Ui.ItemActions[
                Ui.Dropdown.End[
                    Ui.Button.Sm.Ghost.IconTrailing(Ui.IconName.ChevronDown).Inset(Ui.Inset.Right)[role],
                    Ui.Menu[
                        Ui.MenuRadioGroup.Value(role)[Ui.MenuRadio.Value("Admin")["Admin"], Ui.MenuRadio.Value("Member")["Member"]],
                        Ui.MenuSeparator,
                        Ui.MenuItem.Danger["Remove from team…"]
                    ]
                ]
            ]
        ];

    private static Component Options(string label) =>
        Ui.Button.Sm.Ghost.Icon(Ui.IconName.EllipsisHorizontal).Attributes(("aria-label", label));

    private static Component App(string name, string used) =>
        Ui.Item[
            Ui.ItemHeading[name],
            Ui.ItemActions[
                Ui.Text[used],
                Ui.Dropdown.End[Options(name + " options"), Ui.Menu[Ui.MenuItem.Danger["Disconnect…"]]]
            ]
        ];

    private static Component Team(string initials, string name, string boards, string role) =>
        Ui.Item[
            Ui.ItemMedia[Ui.Avatar.Sm.Initials(initials)],
            Ui.ItemHeading[name],
            Ui.ItemSubheading[boards],
            Ui.ItemActions[Ui.Badge.Sm[role]]
        ];

    private static Component Notification(string heading, string text, bool on) =>
        Ui.Item[
            Ui.ItemHeading[heading],
            Ui.ItemSubheading[text],
            Ui.ItemActions[Switch(heading, on)]
        ];
}
