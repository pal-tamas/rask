namespace Rask.Site.Features.UiKit;

/// <summary>
///     daisyUI's Data display category, drawn with the kit.
/// </summary>
/// <remarks>
///     Most of this category is static. The accordions open and close in the browser with no state at all;
///     what holds state holds it here, in plain fields: whether the owned accordion is open, and what the
///     badges that are pressed or removed have been asked to do.
/// </remarks>
public sealed partial class UiKitDataDisplayDemo : Component
{
    private const string Refund =
        "If you are not satisfied with your purchase, we offer a 30-day money-back guarantee. Please contact our support team for assistance.";

    private const string Bulk =
        "Yes, we offer special discounts for bulk orders. Please reach out to our sales team with your requirements.";

    private const string Tracking =
        "Once your order is shipped, you will receive an email with a tracking number. Use this number to track your order on our website.";

    private int _amount = 1;
    private readonly List<string> _roles = ["Admin", "Editor", "Billing"];

    private static readonly Ui.Color?[] BadgeColors =
    [
        null, Ui.Color.Red, Ui.Color.Orange, Ui.Color.Amber, Ui.Color.Yellow, Ui.Color.Lime, Ui.Color.Green,
        Ui.Color.Emerald, Ui.Color.Teal, Ui.Color.Cyan, Ui.Color.Sky, Ui.Color.Blue, Ui.Color.Indigo,
        Ui.Color.Violet, Ui.Color.Purple, Ui.Color.Fuchsia, Ui.Color.Pink, Ui.Color.Rose,
    ];

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        BadgeSection(),
        CardSection(),
        KanbanSection(),
        AccordionSection(),
        AuraSection(),
        TextRotateSection(),
        Hover3DSection(),
        HoverGallerySection(),
        CardsFiguresEmptySection(),
        ChartSection(),
        TableSection(),
        TimelineSection(),
        RestOfCategorySection()
    ];

    private Component BadgeSection() =>
        Section(
            "Badge",
            "Flux UI's badge, example for example: a status, a category or a count in any Tailwind colour. "
            + "It is a <div> until it is told to be a button, and a close button inside it makes it removable.",
            Div.Data(Testid("ui-badge")).Class("flex flex-col gap-5")[
                BadgeRow("intro", "Default", Ui.Badge.Color(Ui.Color.Lime)["New"]),
                BadgeRow("sizes", "Sizes",
                    Ui.Badge.Sm["Small"],
                    Ui.Badge["Default"],
                    Ui.Badge.Lg["Large"]),
                BadgeRow("icons", "Icons",
                    Ui.Badge.Icon(Ui.IconName.UserCircle)["Users"],
                    Ui.Badge.Icon(Ui.IconName.DocumentText)["Files"],
                    Ui.Badge.IconTrailing(Ui.IconName.VideoCamera)["Videos"]),
                BadgeRow("rounded", "Rounded", Ui.Badge.Rounded().Icon(Ui.IconName.User)["Users"]),
                BadgeRow("button", "As button",
                    Ui.Badge
                        .As(Ui.BadgeAs.Button)
                        .Rounded()
                        .Icon(Ui.IconName.Plus)
                        .Lg
                        .Data(Testid("ui-badge-amount"))
                        .OnClick(() => { _amount++; })[$"Amount {_amount}"]),
                BadgeRow("close", "With close button", [.. _roles.Select(RemovableBadge)]),
                BadgeRow("colors", "Colors", [.. BadgeColors.Select(color =>
                    Ui.Badge.Key(BadgeColorName(color)).Color(color)[BadgeColorName(color)])]),
                BadgeRow("solid", "Solid variant", [.. BadgeColors.Select(color =>
                    Ui.Badge.Key(BadgeColorName(color)).Solid.Color(color)[BadgeColorName(color)])]),
                Div.Key("inset").Data(Testid("ui-badge-inset"))[
                    Div.Class("text-base font-medium")[
                        "Page builder ",
                        Ui.Badge.Color(Ui.Color.Lime).Inset(Ui.Inset.Top | Ui.Inset.Bottom)["New"]
                    ],
                    P.Class("mt-2 text-sm text-ui-muted")["Easily author new pages without leaving your browser."]
                ]
            ]);

    private Component RemovableBadge(string role) =>
        Ui.Badge.Key(role)[
            role,
            Ui.BadgeClose.Aria("label", "Remove " + role).OnClick(() => { _roles.Remove(role); })
        ];

    private static string BadgeColorName(Ui.Color? color) => color?.ToString() ?? "Zinc";

    private static Component BadgeRow(string key, string caption, params Component[] badges) =>
        Div.Key(key)[
            P.Class("mb-2 text-xs text-ui-muted")[caption],
            Div.Class("flex flex-wrap items-end gap-2")[badges]
        ];

    private static Component AccordionSection() =>
        Section(
            "Accordion",
            "Flux's accordion, example for example. An item is a <details>, so it opens with a click, Enter "
            + "or Space and no handler at all, and the browser's find-in-page opens the item holding a match.",
            Div.Data(Testid("ui-accordion")).Class("grid max-w-4xl gap-x-12 gap-y-8 md:grid-cols-2")[
                Example("basic", "Heading and content",
                    Ui.Accordion[
                        Ui.AccordionItem[
                            Ui.AccordionHeading["What's your refund policy?"],
                            Ui.AccordionContent[Refund]
                        ],
                        Ui.AccordionItem[
                            Ui.AccordionHeading["Do you offer any discounts for bulk purchases?"],
                            Ui.AccordionContent[Bulk]
                        ],
                        Ui.AccordionItem[
                            Ui.AccordionHeading["How do I track my order?"],
                            Ui.AccordionContent[Tracking]
                        ]
                    ]),
                Example("shorthand", "Shorthand", Ui.Accordion[Questions()]),
                Example("transition", "With transition", Ui.Accordion.Transition()[Questions()]),
                Example("findable", "Findable content — search the page for “signature”",
                    Ui.Accordion[
                        Ui.AccordionItem.Heading("Where do you ship?")["We ship to addresses throughout the United States and Canada."],
                        Ui.AccordionItem.Heading("Do I need to be home for delivery?")["Orders over $500 require a signature upon delivery."],
                        Ui.AccordionItem.Heading("Can I change my order?")["Contact our support team before your order has shipped."]
                    ]),
                Example("disabled", "Disabled",
                    Ui.Accordion[
                        Ui.AccordionItem.Heading("What's your refund policy?")["It all depends how nice you are to me in your email."],
                        Ui.AccordionItem.Heading("Do you offer PPP discounts?").Disabled()[Bulk],
                        Ui.AccordionItem.Heading("How do I track my order?")["What do YOU think?"]
                    ]),
                Example("exclusive", "Exclusive", Ui.Accordion.Exclusive()[Questions()]),
                Example("expanded", "Expanded", Ui.Accordion[Questions(expanded: true)]),
                Example("reverse", "Leading icon", Ui.Accordion.Reverse[Questions()])
            ]);

    private static Component Example(string key, string title, Component accordion) =>
        Div.Key(key).Data(Testid("ui-accordion-" + key))[
            P.Class("mb-3 text-xs font-medium uppercase tracking-wide text-ui-muted")[title],
            accordion
        ];

    private static Component[] Questions(bool expanded = false) =>
    [
        Ui.AccordionItem.Heading("What's your refund policy?")[Refund],
        Ui.AccordionItem.Heading("Do you offer any discounts for bulk purchases?").Expanded(expanded)[Bulk],
        Ui.AccordionItem.Heading("How do I track my order?")[Tracking]
    ];

    private static Component AuraSection() =>
        Section(
            "Aura",
            "Decoration, and only decoration — it says nothing a reader who cannot see it would miss. "
            + "Use it on the one thing a surface is steering towards.",
            Div.Data(Testid("ui-aura")).Class("flex flex-wrap gap-6")[
                Aura("holo", Ui.AuraStyle.Holo, "Holo"),
                Aura("gold", Ui.AuraStyle.Gold, "Gold"),
                Aura("rainbow", Ui.AuraStyle.Rainbow, "Rainbow")
            ]);

    private static Component TextRotateSection() =>
        Section(
            "Text rotate",
            "One slot of text cycling through several words. Every word is in the markup, so the phrase "
            + "has to make sense with all of them — this rotates a word, it does not rewrite a sentence.",
            P.Class("text-2xl font-semibold tracking-tight").Data(Testid("ui-text-rotate"))[
                "Ship it ",
                Ui.TextRotate.Words(["fast", "typed", "small", "whole"]).Class("text-primary"),
                "."
            ]);

    private static Component Hover3DSection() =>
        Section(
            "Hover 3D",
            "Pointer-only by construction: there is no hover on a touch screen and none from a "
            + "keyboard, so nothing may depend on the tilt.",
            Div.Data(Testid("ui-hover-3d")).Class("max-w-xs")[
                Ui.Hover3d[
                    Ui.Card[Ui.CardHeader[Ui.CardHeading.Level(2)["Tilt me"]], Ui.CardBody[P["The content is complete without the effect."]]]
                ]
            ]);

    private static Component HoverGallerySection() =>
        Section(
            "Hover gallery",
            "Several images in the space of one. The first is what shows at rest — and on a touch "
            + "screen it is the only one anybody sees, so put the one that works alone first.",
            Div.Data(Testid("ui-hover-gallery")).Class("max-w-sm")[
                Ui.HoverGallery.Class("h-48 rounded-xl")[
                    Swatch("one", "bg-primary"),
                    Swatch("two", "bg-secondary"),
                    Swatch("three", "bg-accent")
                ]
            ]);

    private static Component CardsFiguresEmptySection() =>
        Section(
            "Cards, figures and empty states",
            "What an operator screen is made of. A card wrapped in a link is one link, figures and all — so "
            + "nothing inside it may be a button. A mono badge wraps a long token instead of widening its "
            + "row, a code block can say what it holds, and an empty state gives the answer before the reason.",
            Div.Data(Testid("ui-console-pieces"))[
                Ui.Grid[
                    NavLink.Key("queue").Href(PageMeta.LinkTo(Routes.UiKitDataGridPage()))[
                        Ui.Card.Class("hover:bg-zinc-50 dark:hover:bg-zinc-700")[
                            Ui.CardHeader[
                                Ui.CardHeading.Level(2).Class("flex items-center gap-2")[Ui.Icon.Name(Ui.IconName.Cog6Tooth).Class("opacity-60"), "Jobs"],
                                Ui.CardSubheading[Ui.StatusDot.Label("2 failed").Error]
                            ],
                            Ui.CardBody[
                                Ui.MetricRow.Columns(2)[
                                    Ui.Metric.Key("outstanding").Label("Outstanding").Value("12"),
                                    Ui.Metric.Key("failed").Label("Failed").Value("2").Error
                                        .Caption("dead after 5 attempts")
                                ]
                            ]
                        ]
                    ],
                    Ui.Card
                        .Key("detail")[Ui.CardHeader[Ui.CardHeading.Level(2)["A failed job"], Ui.CardActions[Ui.Badge.Class("font-mono max-w-full break-all whitespace-normal!")["requestId=0HN8Q2V3R1T0K:00000001"]]], Ui.CardBody[
                        Ui.Code.Content("System.TimeoutException: The SMTP server did not answer in 30 seconds.")
                            .Label("Last error")
                            .Error
                    ]],
                    Ui.Card.Key("empty")[
                        Ui.Empty.Title("Nothing stored matches")
                            .Detail("Retention drops entries by age and by count.")
                    ]
                ]
            ]);

    private static Component TimelineSection() =>
        Section(
            "Timeline",
            "Flux's timeline: events or steps in order, down the page or across it, with the line drawn "
            + "between their indicators. A list, so there is nothing to hold in C#.",
            UiKitTimelineDemo);

    private static Component RestOfCategorySection() =>
        Section(
            "The rest of the category",
            "Static, and covered by unit tests for their class composition.",
            Div.Data(Testid("ui-display-rest")).Class("flex flex-wrap items-center gap-3")[
                Ui.Badge.Key("badge").Color(Ui.Color.Blue)["Beta"],
                Ui.Kbd.Key("kbd").Text("⌘K").Sm,
                // What FullText.Snippet returns for a search of "sqlite fast": matches between U+E000 and U+E001.
                Span.Key("highlight").Data(Testid("ui-highlight"))[
                    Ui.Highlight.Text("…SQLite is small, and fast…")
                ],
                Ui.StatusDot.Key("status").Label("Healthy").Success,
                Ui.Countdown.Key("countdown").Value(42).Label("seconds left"),
                Ui.ChatBubble.Key("chat").Message("On my way").Author("Ada").When("09:14")
            ]);

    private static AttrBag Testid(string value) => new("testid", value);

    private static Component Aura(string key, Ui.AuraStyle style, string label) =>
        Div.Key(key)[
            Ui.Aura.Style(style).Lg[
                Div.Class("rounded-xl border border-base-300 bg-base-100 px-6 py-4 text-sm font-medium")[
                    label
                ]
            ]
        ];

    private static Rask.Core.HTMLDivElement Swatch(string key, string colour) =>
        Div.Key(key).Class($"h-full w-full {colour}");

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
