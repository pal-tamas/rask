namespace Rask.Site.Features.UiKit;

// Flux UI's card page, example by example: https://fluxui.dev/components/card
public sealed partial class UiKitDataDisplayDemo
{
    private static Component CardSection() =>
        Section(
            "Card",
            "A container for related content. Give it a header, a body and a footer and it handles the spacing, "
            + "dividers and corners between them; Body decides how they are set apart, Variant the surface and Size "
            + "the room. The parts are optional: anything put straight inside is padded evenly.",
            Div.Data(Testid("ui-card")).Class("grid gap-6")[
                Div.Key("first").Class("grid gap-6 md:grid-cols-2")[Profile(), Div.Class("grid content-start gap-6")[Simple(), Linked()]],
                Div.Key("variants").Class("grid gap-4 sm:grid-cols-3")[
                    Surface(Ui.Card.Sm, "default", "Raised, for primary content."),
                    Surface(Ui.Card.Sm.Muted, "muted", "A quieter tint for secondary panels."),
                    Surface(Ui.Card.Sm.Soft, "soft", "The faintest tint, for light grouping."),
                    Surface(Ui.Card.Sm.Outline, "outline", "Just an edge, over whatever's behind it."),
                    Surface(Ui.Card.Sm.Filled, "filled", "A tint with no edge, for inline surfaces.")
                ],
                Div.Key("sizes").Class("grid items-start gap-6 sm:grid-cols-2")[
                    Sized(Ui.Card.Xs, "xs", "Compact, for dense lists and sidebars."),
                    Sized(Ui.Card.Sm, "sm", "Tight, for small widgets and stats."),
                    Sized(Ui.Card.Md, "md", "The default, for most content."),
                    Sized(Ui.Card.Lg, "lg", "Roomy, for forms and settings.")
                ],
                Div.Key("treatments").Class("grid items-start gap-6 sm:grid-cols-2 lg:grid-cols-3")[
                    Treatment(Ui.Card.Seamless, "Seamless"),
                    Treatment(Ui.Card.Inset.Soft, "Inset"),
                    Treatment(Ui.Card.Flush.Soft, "Flush"),
                    Treatment(Ui.Card.Divided, "Divided"),
                    Treatment(Ui.Card.Divided.Divider(Ui.CardDivider.Inset), "Divided, inset"),
                    Treatment(Ui.Card.Separated, "Separated")
                ],
                Div.Key("more").Class("grid items-start gap-6 md:grid-cols-3")[Bleeding(), Outside(), Billing()]
            ]);

    private static Component Profile() =>
        Ui.Card.Inset.Soft.Lg[
            Ui.CardHeader[
                Ui.CardHeading["Profile"],
                Ui.CardSubheading["This is how others will see you"],
                Ui.CardActions[Ui.Button["Edit"]]
            ],
            Ui.CardBody[P.Class("text-sm")["Olivia Martin"], P.Class("mt-4 text-sm text-zinc-500")["A few words about yourself"]],
            Ui.CardFooter[
                Ui.Text["Last saved 2 minutes ago"],
                Ui.CardActions[Ui.Button["Cancel"], Ui.Button["Save"]]
            ]
        ];

    private static Component Simple() =>
        Ui.Card[
            Ui.CardHeading.Lg["Are you sure?"],
            P.Class("mt-2 mb-4 text-sm text-zinc-500 dark:text-white/70")["Your post will be deleted permanently. This action cannot be undone."],
            Ui.Button["Delete"]
        ];

    // Flux's link card: the link goes around a small card, and the hover is the call site's.
    private static Component Linked() =>
        A.Href("#card").Aria("label", "Latest on our blog").Class("block")[
            Ui.Card.Xs.Class("hover:bg-zinc-50 dark:hover:bg-zinc-700")[
                Ui.CardHeading["Latest on our blog"],
                P.Class("mt-2 text-sm text-zinc-500 dark:text-white/70")["Stay up to date with our latest insights, tutorials, and product updates."]
            ]
        ];

    private static Component Surface(UiCard card, string name, string text) =>
        card.Key(name)[Ui.CardHeading[name], P.Class("mt-1 text-sm text-zinc-500 dark:text-white/70")[text]];

    private static Component Sized(UiCard card, string name, string text) =>
        card.Key(name).Separated[
            Ui.CardHeader[Ui.CardHeading[name], Ui.CardActions[Ui.Button["Edit"]]],
            Ui.CardBody[P.Class("text-sm text-zinc-500 dark:text-white/70")[text]]
        ];

    private static Component Treatment(UiCard card, string name) =>
        card.Key(name)[
            Ui.CardHeader[Ui.CardHeading[name], Ui.CardSubheading["Choose what you hear about"]],
            Ui.CardBody[P.Class("text-sm")["Product updates"], P.Class("mt-4 text-sm")["Weekly digest"], P.Class("mt-4 text-sm")["Security alerts"]],
            Ui.CardFooter[Ui.Text["Changes apply at once"]]
        ];

    private static Component Bleeding() =>
        Ui.Card.Inset[
            Ui.CardBody[
                Ui.CardBleed[Div.Class("aspect-video w-full bg-linear-to-br from-emerald-700 to-emerald-200").Aria("hidden", "true")]
            ],
            Ui.CardFooter[Ui.CardHeading["Morning hike"], Ui.CardSubheading["Sept 12"]]
        ];

    private static Component Outside() =>
        Div[
            Ui.CardHeader[Ui.CardHeading.Lg["Security"], Ui.CardActions[Ui.Button["Activity log"]]],
            Ui.Card[
                Ui.CardHeading["Password"],
                P.Class("mt-1 text-sm text-zinc-500 dark:text-white/70")["Last changed 3 months ago"]
            ]
        ];

    private static Component Billing() =>
        Ui.Card.Inset.Soft[
            Ui.CardHeader[Ui.CardHeading["Billing"], Ui.CardSubheading["Manage your plan and payments"]],
            Ui.CardBody[
                Ui.CardHeader[
                    Ui.CardHeading["Pro plan"],
                    Ui.CardSubheading["$24 per month, renews Oct 12"],
                    Ui.CardActions[Ui.Button["Change plan"]]
                ],
                P.Class("text-sm text-zinc-500 dark:text-white/70")["8 of 10 seats used"]
            ],
            Ui.CardFooter[Ui.Text["Invoices are emailed to billing@acme.com"]]
        ];
}
