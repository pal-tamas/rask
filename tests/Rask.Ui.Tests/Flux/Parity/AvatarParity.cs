using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/avatar</c>, example for example.
/// </summary>
/// <remarks>
///     <para>
///     Flux's page draws fewer avatars than its markdown lists in three places — one under "Tooltip", three
///     under "Initials", seven under "Auto color" — and the page is what is measured, so those are what is here.
///     </para>
///     <para>
///     The six "Examples" set avatars among other pages' components. Headings, text, stars and buttons beside
///     an avatar are left out (unmarked, and <c>flux parity</c> says so). Where Flux puts the avatars INSIDE
///     another component — a tooltip, a table, a card, a select, a dropdown — that component is the real one.
///     </para>
/// </remarks>
public sealed partial class AvatarParity : FluxParity
{
    private const string Taylor = "https://unavatar.io/x/taylorotwell";

    private static Component Assignee(string src, string name) =>
        Li.Style("display:flex;align-items:center;gap:8px")[Ui.Avatar.Xs.Src(src), Ui.Heading[name]];

    // What an app's own Tailwind build emits for the classes the last examples hand to `class`, under names of this
    // page's own: unlayered, a real utility's name would beat the kit's `dark:` twin of it.
    private const string AppUtilities =
        "<style>.parity-member{display:flex;align-items:center;gap:1rem}.parity-column{display:flex;flex-direction:column}"
        + ".parity-ml-1{margin-left:.25rem}.parity-actions{display:flex;justify-content:flex-end;align-items:center;gap:.5rem}"
        + ".parity-fit{min-width:fit-content;max-width:fit-content}.parity-shrink-0{flex-shrink:0}"
        + ".parity-option{display:flex;align-items:center;gap:.5rem;white-space:nowrap}"
        + ".parity-card{position:relative;max-width:15rem}.parity-corner{position:absolute;top:0;right:0;padding:.5rem}"
        + ".parity-mt-2{margin-top:.5rem}.parity-mt-3{margin-top:.75rem}"
        + ".parity-stats{display:flex;gap:1rem;margin-top:.75rem}.parity-stat{display:flex;gap:.5rem;align-items:center}"
        + ".parity-docs-xs{width:100%;max-width:20rem;padding-inline:3rem}"
        + "@media (width < 40rem){.parity-docs-xs{padding-inline:2rem}.parity-member{gap:.5rem}.parity-member-avatar{width:2rem;height:2rem}.parity-phone-hidden{display:none}}"
        + "</style>";

    // One row of the members table: who, and the role they hold.
    private static Component Member(string src, string name, Component? badge, string email, string role) =>
        Ui.TableRow[
            Ui.TableCell[
                Div.Class("parity-member")[
                    Ui.Avatar.Circle().Lg.Class("parity-member-avatar").Src(src),
                    Div.Class("parity-column")[
                        badge is null ? Ui.Heading[name] : Ui.Heading[name + " ", badge],
                        Ui.Text.Class("parity-phone-hidden")[email]
                    ]
                ]
            ],
            Ui.TableCell[
                Div.Class("parity-actions")[
                    Ui.Select.Value(role).Size(Ui.SelectSize.Sm).Class("parity-fit")[
                        Ui.SelectOption.Value("admin")["Admin"],
                        Ui.SelectOption.Value("member")["Member"],
                        Ui.SelectOption.Value("guest")["Guest"]
                    ],
                    Ui.Button.Sm.Subtle.Icon(Ui.IconName.Trash).Class("parity-shrink-0")
                ]
            ]
        ];

    private static Component Assignable(string src, string name) =>
        Ui.SelectOption.Value(name)[Div.Class("parity-option")[Ui.Avatar.Circle().Xs.Src(src), " " + name]];

    public override string Page => "avatar";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Row(Raw.Value(NavigationStandIns.Sheet), Div[Ui.Avatar.Src(NavigationStandIns.Caleb)]));

        yield return ("tooltip", Row(Ui.Avatar.Tooltip("Caleb Porzio").Src(NavigationStandIns.Caleb)));

        yield return ("initials", Row(
            Ui.Avatar.Name("Caleb Porzio"),
            Ui.Avatar.Name("calebporzio"),
            Ui.Avatar.Name("calebporzio").InitialsSingle()));

        yield return ("size", Row(
            Ui.Avatar.Xl.Src(NavigationStandIns.Caleb),
            Ui.Avatar.Lg.Src(NavigationStandIns.Caleb),
            Ui.Avatar.Src(NavigationStandIns.Caleb),
            Ui.Avatar.Sm.Src(NavigationStandIns.Caleb),
            Ui.Avatar.Xs.Src(NavigationStandIns.Caleb)));

        yield return ("icon", Row(
            Ui.Avatar.Icon(Ui.IconName.User),
            Ui.Avatar.Icon(Ui.IconName.Phone),
            Ui.Avatar.Icon(Ui.IconName.ComputerDesktop)));

        yield return ("colors", Div.Style("display:flex;flex-wrap:wrap;gap:8px;justify-content:center")[
            Enum.GetValues<Ui.Color>().Take(17).Select(Component (color) => Ui.Avatar.Key(color.ToString()).Name("Caleb Porzio").Color(color))
        ]);

        yield return ("auto-color", Row(
            Ui.Avatar.Name("Caleb Porzio").ColorAuto(),
            Ui.Avatar.Initials("MJ").ColorAuto(),
            Ui.Avatar.Initials("KC").ColorAuto(),
            Ui.Avatar.Initials("KN").ColorAuto(),
            // The last three are `max-sm:hidden` on Flux's page: a phone shows four.
            Ui.Avatar.Initials("KS").ColorAuto().Class("parity-max-sm-hidden"),
            Ui.Avatar.Initials("BP").ColorAuto().Class("parity-max-sm-hidden"),
            Ui.Avatar.Initials("AB").ColorAuto().Class("parity-max-sm-hidden")));

        yield return ("circle", Row(Div[Ui.Avatar.Circle().Src(NavigationStandIns.Caleb)]));

        yield return ("badge", Row(
            Ui.Avatar.Badge("").BadgeColor(Ui.Color.Green).Src(NavigationStandIns.Caleb),
            Ui.Avatar.Badge("").BadgeColor(Ui.Color.Zinc).BadgePosition(Ui.AvatarBadgePosition.TopRight).BadgeCircle()
                .BadgeVariant(Ui.AvatarBadgeVariant.Outline).Src(NavigationStandIns.Caleb),
            Ui.Avatar.Badge("25").Src(NavigationStandIns.Caleb),
            Ui.Avatar.Circle().Badge("👍").BadgeCircle().Src(NavigationStandIns.Caleb),
            Ui.Avatar.Circle().Badge(Img.Src(NavigationStandIns.Hugo).Alt("").Style("width:12px;height:12px;max-width:100%")).Src(NavigationStandIns.Caleb)));

        yield return ("groups", Row(
            Ui.AvatarGroup[
                Ui.Avatar.Src(NavigationStandIns.Caleb),
                Ui.Avatar.Src(NavigationStandIns.Hugo),
                Ui.Avatar.Src(NavigationStandIns.Josh),
                Ui.Avatar["3+"]
            ],
            Ui.AvatarGroup.Class("ring-ground")[
                Ui.Avatar.Circle().Src(NavigationStandIns.Caleb),
                Ui.Avatar.Circle().Src(NavigationStandIns.Hugo),
                Ui.Avatar.Circle().Src(NavigationStandIns.Josh),
                Ui.Avatar.Circle()["3+"]
            ]));

        yield return ("as-button", Row(Div[Ui.Avatar.As(Ui.AvatarAs.Button).Src(NavigationStandIns.Caleb)]));

        yield return ("as-link", Row(Div[Ui.Avatar.Href("https://x.com/calebporzio").Src(NavigationStandIns.Caleb)]));

        // Testimonial.
        yield return ("examples", Row(Ui.Avatar.Lg.Src(Taylor)));

        // Grouped feature.
        yield return ("examples", Row(Ui.AvatarGroup[
            Ui.Avatar.Circle().Lg.Src(Taylor),
            Ui.Avatar.Circle().Lg.Src("https://unavatar.io/x/adamwathan"),
            Ui.Avatar.Circle().Lg.Src("https://unavatar.io/x/jeffrey_way"),
            Ui.Avatar.Circle().Lg.Src("https://unavatar.io/x/stauffermatt")
        ]));

        // Members table.
        yield return ("examples", Row(Raw.Value(AppUtilities), Div.Style("width:100%;max-width:542px")[Ui.Table[
            Ui.TableRows[
                Member(NavigationStandIns.Caleb, "Caleb Porzio",
                    Ui.Badge.Sm.Color(Ui.Color.Blue).Class("parity-ml-1 parity-phone-hidden")["You"], "caleb@laravel-livewire.com", "admin"),
                Member(NavigationStandIns.Hugo, "Hugo Sainte-Marie", null, "hugo@example.com", "member"),
                Member(NavigationStandIns.Josh, "Josh Hanley", null, "josh@example.com", "member")
            ]
        ]]));

        // Assignees list.
        yield return ("examples", Row(Div.Style("width:384px")[Ui.Card[
            Div.Style("display:flex;justify-content:space-between;align-items:center")[
                Ui.Heading["Assignees"],
                Ui.Button.Sm.Subtle.Icon(Ui.IconName.Plus).Inset(Ui.Inset.Top | Ui.Inset.Bottom)
            ],
            Ui.Separator.Subtle.Class("parity-rule"),
            Ul.Style("display:flex;flex-direction:column;gap:12px")[
                Assignee(NavigationStandIns.Caleb, "Caleb Porzio"),
                Assignee(NavigationStandIns.Hugo, "Hugo Sainte-Marie"),
                Assignee(NavigationStandIns.Josh, "Josh Hanley"),
                Assignee("https://unavatar.io/github/jasonlbeggs", "Jason Beggs")
            ]
        ]]));

        // Select options.
        // The docs page sets this one in a column of its own, `w-full max-w-xs px-8 sm:px-12`.
        yield return ("examples", Row(Div.Class("parity-docs-xs")[Ui.Select.Value("Caleb Porzio").Listbox.Label("Assign to")[
            Assignable(NavigationStandIns.Caleb, "Caleb Porzio"),
            Assignable(NavigationStandIns.Hugo, "Hugo Sainte-Marie"),
            Assignable(NavigationStandIns.Josh, "Josh Hanley"),
            Assignable("https://unavatar.io/github/jasonlbeggs", "Jason Beggs")
        ]]));

        // User popover.
        yield return ("examples", Row(Div[Ui.Dropdown.Hover().Bottom.Center[
            Ui.Avatar.As(Ui.AvatarAs.Button).Name("calebporzio").Src(NavigationStandIns.Caleb),
            Ui.Popover.Class("parity-card")[
                Div.Class("parity-corner")[Ui.Button.Filled.Sm.Icon(Ui.IconName.UserPlus)["Follow back"]],
                Ui.Avatar.Xl.Name("calebporzio").Src(NavigationStandIns.Caleb),
                Ui.Heading.Class("parity-mt-2")["Caleb Porzio"],
                Ui.Text["@calebporzio ", Ui.Badge.Color(Ui.Color.Zinc).Sm.Inset(Ui.Inset.Top | Ui.Inset.Bottom)["Follows you"]],
                Ui.Text.Class("parity-mt-3")[
                    "I'm a full stack developer with a passion for building web applications. Currently working on a new project called ",
                    Ui.Link.Href("https://fluxui.dev")["Flux"],
                    "."
                ],
                Div.Class("parity-stats")[
                    Div.Class("parity-stat")[Ui.Heading["1.2k"], " ", Ui.Text["Followers"]],
                    Div.Class("parity-stat")[Ui.Heading["1.2k"], " ", Ui.Text["Following"]]
                ]
            ]
        ]]));
    }
}
