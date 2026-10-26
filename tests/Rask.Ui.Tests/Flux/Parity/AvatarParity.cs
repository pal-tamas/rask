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
///     an avatar are left out (unmarked, and <c>parity.mjs</c> says so). Where Flux puts the avatars INSIDE
///     another component — a tooltip, a table, a card, a select, a dropdown — that component is one box of its
///     size, with the avatars in it.
///     </para>
/// </remarks>
public sealed partial class AvatarParity : FluxParity
{
    private const string Taylor = "https://unavatar.io/x/taylorotwell";

    private static Component Assignee(string src, string name) =>
        Li.Style("display:flex;align-items:center;gap:8px")[Ui.Avatar.Xs.Src(src), Ui.Heading[name]];

    // What the stand-ins measure on a phone: the members table, its emails `max-sm:hidden`, is shorter; the
    // select, set in 16px there, is wider.
    private const string PhoneSizes =
        "<style>.parity-members{height:218px}.parity-select{width:224px}"
        + "@media (width < 40rem){.parity-members{height:170px}.parity-select{width:228px}}</style>";

    // NavigationStandIns.Skipped, with the class that sizes it.
    private static Component StandIn(string component, string size, string style, params Component[] children) =>
        Div.Class(size).Style(style).Attributes(("data-ui-" + component, null), ("data-parity-skip", null))[children];

    public override string Page => "avatar";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Row(Raw.Value(NavigationStandIns.Sheet), Raw.Value(PhoneSizes), Div[Ui.Avatar.Src(NavigationStandIns.Caleb)]));

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
        yield return ("examples", Row(StandIn("table", "parity-members", "width:100%;max-width:542px;display:flex;flex-direction:column;gap:25px",
            Ui.Avatar.Circle().Lg.Src(NavigationStandIns.Caleb),
            Ui.Avatar.Circle().Lg.Src(NavigationStandIns.Hugo),
            Ui.Avatar.Circle().Lg.Src(NavigationStandIns.Josh))));

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
        yield return ("examples", Row(StandIn("field", "parity-select", "height:76px",
            Ui.Avatar.Circle().Xs.Src(NavigationStandIns.Caleb))));

        // User popover.
        yield return ("examples", Row(NavigationStandIns.Skipped("dropdown", "display:flex",
            Ui.Avatar.As(Ui.AvatarAs.Button).Name("calebporzio").Src(NavigationStandIns.Caleb))));
    }
}
