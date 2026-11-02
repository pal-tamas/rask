using Rask.Core;

namespace Rask.UiTests.Flux;

/// <summary>
///     Flux UI's card page, example by example: <c>https://fluxui.dev/components/card</c>.
/// </summary>
/// <remarks>
///     The examples put other Flux components inside the cards — fields, headings, text, a progress bar, buttons,
///     a table — and each is the real one, compared whole with the card's own parts. The bleeding photograph is
///     no component: it is a <see cref="Photo" />, a box of its size marked <c>data-parity-skip</c>.
/// </remarks>
public sealed partial class CardParity : FluxParity
{
    // `my-4` and `my-6` on the separators there: an app's own utilities.
    private const string Spacing = "<style>.parity-mt-1{margin-top:4px}.parity-mt-2{margin-top:8px}.parity-mb-4{margin-bottom:16px}"
        + ".parity-my-4{margin-block:16px}.parity-my-6{margin-block:24px}.parity-space-y-4>:not(:last-child){margin-bottom:16px}"
        + ".parity-space-y-6>:not(:last-child){margin-bottom:24px}.parity-space-y-2>:not(:last-child){margin-bottom:8px}"
        + ".parity-row{display:flex;justify-content:space-between}.parity-gap-3{display:flex;align-items:center;gap:12px}"
        + ".parity-gap-2{display:flex;align-items:center;gap:8px}.parity-ml-auto{margin-left:auto}"
        + ".parity-faint{color:oklch(70.5% .015 286.067)}"
        // The grids of the variants and the sizes: one column on a phone, stepped in 32px each side from lg up.
        + ".parity-grid{display:grid;max-width:768px;margin-inline:auto}"
        + "@media (width >= 40rem){.parity-cols-2{grid-template-columns:repeat(2,minmax(0,1fr))}.parity-cols-3{grid-template-columns:repeat(3,minmax(0,1fr))}}"
        + "@media (width >= 64rem){.parity-grid{padding-inline:32px}}</style>";

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
            Ui.CardBody.Class("parity-space-y-6")[
                Ui.Input.Value("Olivia Martin").Label("Name"),
                Ui.Textarea.Of<string>().Label("Bio").Rows(2).Placeholder("A few words about yourself")
            ],
            Ui.CardFooter[
                Ui.Text["Last saved 2 minutes ago"],
                Ui.CardActions[
                    Ui.Button.Ghost["Cancel"],
                    Ui.Button.Primary["Save"]
                ]
            ]
        ])];

    private static Component SimpleCard() => Narrow(
        Ui.Card[
            Ui.Heading.Lg["Are you sure?"],
            Ui.Text.Class("parity-mt-2 parity-mb-4")["Your post will be deleted permanently.", Br, "This action cannot be undone."],
            Ui.Button.Danger["Delete"]
        ]);

    private static Component Variants() =>
        Div.Class("parity-grid parity-cols-3").Style("grid-auto-rows:1fr;gap:16px")[
            Variant(Ui.Card.Sm, "default", "Raised, for primary content."),
            Variant(Ui.Card.Sm.Muted, "muted", "A quieter tint for secondary panels."),
            Variant(Ui.Card.Sm.Soft, "soft", "The faintest tint, for light grouping."),
            Variant(Ui.Card.Sm.Outline, "outline", "Just an edge, over whatever's behind it."),
            Variant(Ui.Card.Sm.Filled, "filled", "A tint with no edge, for inline surfaces.")
        ];

    private static Component Variant(UiCard card, string name, string text) =>
        card[Ui.Heading[name], Ui.Text.Class("parity-mt-1")[text]];

    private static Component Sizes() =>
        Div.Class("parity-grid parity-cols-2").Style("gap:24px;align-items:start")[
            Sized(Ui.Card.Xs.Separated, "xs", "Compact, for dense lists and sidebars."),
            Sized(Ui.Card.Sm.Separated, "sm", "Tight, for small widgets and stats."),
            Sized(Ui.Card.Md.Separated, "md", "The default, for most content."),
            Sized(Ui.Card.Lg.Separated, "lg", "Roomy, for forms and settings.")
        ];

    private static Component Sized(UiCard card, string name, string text) =>
        card[
            Ui.CardHeader[
                Ui.CardHeading[name],
                Ui.CardActions[Ui.Button.Sm["Edit"]]
            ],
            Ui.CardBody[Ui.Text[text]]
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
                Ui.CardBleed[Photo()]
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
                Div.Class("parity-space-y-2")[
                    Div.Class("parity-row")[Ui.Text["Seats used"], Ui.Text.Variant(Ui.TextVariant.Strong)["8 of 10"]],
                    Ui.Progress.Value(80)
                ],
                Ui.Separator.Subtle.Class("parity-my-6"),
                Ui.CardHeader[
                    Ui.CardHeading["Payment method"],
                    Ui.CardActions[Ui.Button.Sm.Ghost["Update"]]
                ],
                Div.Class("parity-gap-3")[
                    Ui.Icon.Name(Ui.IconName.CreditCard).Outline.Class("parity-faint"),
                    Ui.Text["Visa ending in 4242 · Expires 08/28"]
                ]
            ],
            Ui.CardFooter[Ui.Text["Invoices are emailed to billing@acme.com"]]
        ]);

    // Flux's example passes the hover as utilities of the docs app's own stylesheet, which the kit's sheet does
    // not carry; the same two colours (zinc-50, zinc-700) are stated here.
    private static Component LinkCard() => Narrow(
        // `hover:` as Tailwind writes it: only where there is a pointer to hover (a phone has none).
        Raw.Value("<style>@media (hover:hover){.parity-link:hover{background-color:oklch(0.985 0 0)}.dark .parity-link:hover{background-color:oklch(0.37 0.013 285.805)}}</style>"),
        A.Href("#").Aria("label", "Latest on our blog").Style("display:block;cursor:pointer")[
            Ui.Card.Xs.Class("parity-link")[
                Ui.Heading.Class("parity-gap-2")[
                    "Latest on our blog ",
                    Ui.Icon.Name(Ui.IconName.ArrowUpRight).Micro.Class("parity-ml-auto parity-faint")
                ],
                Ui.Text.Class("parity-mt-2")["Stay up to date with our latest insights, tutorials, and product updates."]
            ]
        ]);

    /// <summary>The 384px column most of the page's examples sit in.</summary>
    private static Component Narrow(params Component[] items) =>
        Div.Style("max-width:384px;margin:0 auto")[Raw.Value(Spacing), items];

    private static Component Pair(Component first, Component second) =>
        Div.Class("parity-grid parity-cols-2").Style("gap:24px")[first, second];

    // <img class="block aspect-video w-full object-cover">: a picture, no component — the room it takes.
    private static Component Photo() =>
        Div.Data("parity-skip").Style("aspect-ratio:16/9;background:linear-gradient(#5b7a6a,#c9d6c3)");
}
