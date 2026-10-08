using Rask.Core;

namespace Rask.UiTests.Flux;

/// <summary>
///     Flux UI's card page, example by example: <c>https://fluxui.dev/components/card</c>.
/// </summary>
/// <remarks>
///     The examples put other Flux components inside the cards. The buttons and the table are the real
///     ones; the fields and the lines of text whose variant was not looked up are built elsewhere
///     or later, and each is a <see cref="StandIn" /> here: a box the measured size of that neighbour,
///     marked <c>data-parity-skip</c> so the comparison holds it to its box and skips what is inside. Everything
///     that is the card's own — the card, its header, heading, subheading, actions, body, footer and bleed — is
///     the real component and is compared whole.
/// </remarks>
public sealed partial class CardParity : FluxParity
{
    // `my-4` and `my-6` on the separators there: an app's own utilities.
    private const string Spacing = "<style>.parity-mt-1{margin-top:4px}.parity-my-4{margin-block:16px}.parity-my-6{margin-block:24px}.parity-space-y-4>:not(:last-child){margin-bottom:16px}</style>";

    public override string Page => "card";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Intro());
        yield return ("simple-card", SimpleCard());
        yield return ("variants", Variants());
        yield return ("sizes", Sizes());
        yield return ("body-treatments", Narrow(Notifications(Ui.Card.Seamless)));
        yield return ("body-treatments", Narrow(Notifications(Ui.Card.Inset.Soft)));
        yield return ("body-treatments", Narrow(Notifications(Ui.Card.Flush.Soft)));
        yield return ("body-treatments", Pair(Notifications(Ui.Card.Divided), Notifications(Ui.Card.Divided.Divider(Ui.CardDivider.Inset))));
        yield return ("body-treatments", Narrow(Notifications(Ui.Card.Separated)));
        yield return ("bleed", BleedImage());
        yield return ("bleed", BleedTable());
        yield return ("outside-a-card", OutsideACard());
        yield return ("inside-a-body", InsideABody());
        yield return ("link-card", LinkCard());
    }

    // The page's first example sits in the hero, whose line height is 24px where the prose below it has 26px.
    private static Component Intro() => Div.Style("line-height:24px")[Narrow(
        Ui.Card.Inset.Soft.Lg[
            Ui.CardHeader[
                Ui.CardHeading["Profile"],
                Ui.CardSubheading["This is how others will see you"],
                Ui.CardActions[Ui.Button.Sm.Ghost.Icon(Ui.IconName.EllipsisHorizontal).AriaLabel("More options")]
            ],
            Ui.CardBody[
                StandIn("height:75px;margin-bottom:24px", "Name"),
                StandIn("height:101px", "Bio")
            ],
            Ui.CardFooter[
                StandIn("height:20px", "Last saved 2 minutes ago"),
                Ui.CardActions[
                    Ui.Button.Ghost["Cancel"],
                    Ui.Button.Primary["Save"]
                ]
            ]
        ])];

    private static Component SimpleCard() => Narrow(
        Ui.Card[
            StandIn("height:24px", "Are you sure?"),
            StandIn("height:40px;margin:8px 0 16px", "Your post will be deleted permanently."),
            Ui.Button.Danger["Delete"]
        ]);

    private static Component Variants() =>
        Div.Style("display:grid;grid-template-columns:repeat(3,170px);grid-auto-rows:1fr;gap:16px;justify-content:center")[
            Variant(Ui.Card.Sm, "default", 40),
            Variant(Ui.Card.Sm.Muted, "muted", 40),
            Variant(Ui.Card.Sm.Soft, "soft", 40),
            Variant(Ui.Card.Sm.Outline, "outline", 60),
            Variant(Ui.Card.Sm.Filled, "filled", 40)
        ];

    private static Component Variant(UiCard card, string name, int text) =>
        card[StandIn("height:20px", name), StandIn($"height:{text}px;margin-top:4px")];

    private static Component Sizes() =>
        Div.Style("display:grid;grid-template-columns:repeat(2,259px);gap:24px;align-items:start;justify-content:center")[
            Sized(Ui.Card.Xs.Separated, "xs", 40),
            Sized(Ui.Card.Sm.Separated, "sm", 20),
            Sized(Ui.Card.Md.Separated, "md", 20),
            Sized(Ui.Card.Lg.Separated, "lg", 20)
        ];

    private static Component Sized(UiCard card, string name, int text) =>
        card[
            Ui.CardHeader[
                Ui.CardHeading[name],
                Ui.CardActions[Ui.Button.Sm["Edit"]]
            ],
            Ui.CardBody[StandIn($"height:{text}px")]
        ];

    private static Component Notifications(UiCard card) =>
        card[
            Ui.CardHeader[
                Ui.CardHeading["Notifications"],
                Ui.CardSubheading["Choose what you hear about"]
            ],
            // class="space-y-4" on the body is the example's own: stated on the page under a name of its own.
            Ui.CardBody.Class("parity-space-y-4")[
                Ui.Switch.Value(true).Label("Product updates"),
                Ui.Switch.Value(false).Label("Weekly digest"),
                Ui.Switch.Value(true).Label("Security alerts")
            ]
        ];

    private static Component BleedImage() => Narrow(
        Ui.Card.Inset[
            Ui.CardBody[
                Ui.CardBleed[StandIn("aspect-ratio:16/9;background:linear-gradient(#5b7a6a,#c9d6c3)")]
            ],
            Ui.CardFooter[
                Ui.CardHeading["Morning hike"],
                Ui.CardSubheading["Sept 12"],
                Ui.CardActions[Ui.Button.Sm.Ghost.Icon(Ui.IconName.EllipsisHorizontal).AriaLabel("More options")]
            ]
        ]);

    private static Component BleedTable() => Narrow(
        Raw.Value("<style>.parity-in{color:oklch(0.527 0.154 150.069)}.dark .parity-in{color:oklch(0.792 0.209 151.711)}</style>"),
        Ui.Card.Muted.Flush[
            Ui.CardHeader[
                Ui.CardHeading["Past transactions"],
                Ui.CardActions[Ui.Button.Sm.Filled.Icon(Ui.IconName.AdjustmentsHorizontal).AriaLabel("Filter transactions")]
            ],
            Ui.CardBody[
                Ui.Table.Bleed()[
                    Ui.TableRows[
                        Transaction("Blue Bottle Coffee", "Dining", "−$6.75"),
                        Transaction("Lumen Design Co.", "Payroll", "+$3,120.00"),
                        Transaction("Whole Foods Market", "Groceries", "−$84.20"),
                        Transaction("Netflix", "Subscriptions", "−$15.49")
                    ]
                ]
            ]
        ]);

    // class="text-xs", "font-medium tabular-nums" and, on money coming in, green-700 (green-400 in dark)
    // there: an app's own utilities, stated here.
    private static Component Transaction(string payee, string kind, string amount) =>
        Ui.TableRow.Key(payee)[
            Ui.TableCell[Ui.Heading[payee], Ui.Text.Style("font-size:12px;line-height:16px")[kind]],
            Ui.TableCell.End.Class(amount.StartsWith('+') ? "parity-in" : null)
                .Style("font-weight:500;font-variant-numeric:tabular-nums")[amount]
        ];

    private static Component OutsideACard() => Narrow(
        Ui.CardHeader[
            Ui.CardHeading.Lg["Security"],
            Ui.CardActions[Ui.Button.Sm["Activity log"]]
        ],
        Ui.Card[
            Setting("Password", "Last changed 3 months ago", Ui.Button.Sm["Change"]),
            Ui.Separator.Subtle.Class("parity-my-4"),
            Setting("Two-factor authentication", "A code from your authenticator app", Ui.Badge.Sm.Color(Ui.Color.Green)["On"])
        ]);

    // One row of the settings card: a heading over a line of text, and what acts on it at the end.
    private static Component Setting(string name, string detail, Component end) =>
        Div.Style("display:flex;align-items:center;justify-content:space-between;gap:16px")[
            Div[Ui.Heading[name], Ui.Text.Class("parity-mt-1")[detail]],
            end
        ];

    private static Component InsideABody() => Narrow(
        Ui.Card.Inset.Soft[
            Ui.CardHeader[
                Ui.CardHeading["Billing"],
                Ui.CardSubheading["Manage your plan and payments"]
            ],
            Ui.CardBody[
                Ui.CardHeader[
                    Ui.CardHeading["Pro plan"],
                    Ui.CardSubheading["$24 per month, renews Oct 12"],
                    Ui.CardActions[Ui.Button.Sm["Change plan"]]
                ],
                StandIn("height:34px", "Seats used"),
                Ui.Separator.Subtle.Class("parity-my-6"),
                Ui.CardHeader[
                    Ui.CardHeading["Payment method"],
                    Ui.CardActions[Ui.Button.Sm.Ghost["Update"]]
                ],
                StandIn("height:24px", "Visa ending in 4242 · Expires 08/28")
            ],
            Ui.CardFooter[StandIn("height:20px", "Invoices are emailed to billing@acme.com")]
        ]);

    // Flux's example passes the hover as utilities of the docs app's own stylesheet, which the kit's sheet does
    // not carry; the same two colours (zinc-50, zinc-700) are stated here.
    private static Component LinkCard() => Narrow(
        Raw.Value("<style>.parity-link:hover{background-color:oklch(0.985 0 0)}.dark .parity-link:hover{background-color:oklch(0.37 0.013 285.805)}</style>"),
        A.Href("#").Aria("label", "Latest on our blog").Style("display:block;cursor:pointer")[
            Ui.Card.Xs.Class("parity-link")[
                StandIn("height:20px", "Latest on our blog"),
                StandIn("height:40px;margin-top:8px", "Stay up to date with our latest insights, tutorials, and product updates.")
            ]
        ]);

    /// <summary>The 384px column most of the page's examples sit in.</summary>
    private static Component Narrow(params Component[] items) =>
        Div.Style("max-width:384px;margin:0 auto")[Raw.Value(Spacing), items];

    private static Component Pair(Component first, Component second) =>
        Div.Style("display:grid;grid-template-columns:repeat(2,259px);gap:24px;justify-content:center")[first, second];

    /// <summary>
    ///     A neighbouring Flux component that is not the card's: a box of its measured size, compared by that box alone.
    /// </summary>
    private static Component StandIn(string box, string? text = null) =>
        Div.Data("parity-skip").Style(box + ";overflow:hidden;font-size:14px;line-height:20px;white-space:nowrap")[text ?? ""];
}
