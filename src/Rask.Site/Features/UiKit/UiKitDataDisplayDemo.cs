namespace Rask.Site.Features.UiKit;

/// <summary>
///     daisyUI's Data display category, drawn with the kit.
/// </summary>
/// <remarks>
///     Most of this category is static. The accordions open and close in the browser with no state at all;
///     what holds state holds it here, in plain fields. The badge and the card have pages of their own in the UI tab.
/// </remarks>
public sealed partial class UiKitDataDisplayDemo : Component
{
    private const string Refund =
        "If you are not satisfied with your purchase, we offer a 30-day money-back guarantee. Please contact our support team for assistance.";

    private const string Bulk =
        "Yes, we offer special discounts for bulk orders. Please reach out to our sales team with your requirements.";

    private const string Tracking =
        "Once your order is shipped, you will receive an email with a tracking number. Use this number to track your order on our website.";

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        KanbanSection(),
        AccordionSection(),
        ChartSection(),
        TableSection(),
        StableColumnsSection(),
        PagingThatWaitsSection(),
        TimelineSection(),
        CarouselSection(),
        FlagSection(),
        RestOfCategorySection()
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

    private static Component TimelineSection() =>
        Section(
            "Timeline",
            "Flux's timeline: events or steps in order, down the page or across it, with the line drawn "
            + "between their indicators. A list, so there is nothing to hold in C#.",
            UiKitTimelineDemo);

    private static Component CarouselSection() =>
        Section(
            "Carousel",
            "Flux's carousel: slides that scroll sideways and come to rest on one. The browser scrolls and snaps — a "
            + "wheel, a swipe and the keyboard all move it — and the arrows, the indicators and autoplay follow in "
            + "the browser, with nothing sent to the page.",
            UiKitCarouselDemo.Key("carousels"));

    private static Component FlagSection() =>
        Section(
            "Flag",
            "Flux's flag: a country's flag from its two-letter region code, 3:2 or cropped to a circle, in five "
            + "widths. Each is one small file the page fetches when it shows it.",
            UiKitFlagDemo);

    private static Component RestOfCategorySection() =>
        Section(
            "The rest of the category",
            "Static, and covered by unit tests for their class composition.",
            Div.Data(Testid("ui-display-rest")).Class("flex flex-wrap items-center gap-3")[
                Ui.Badge.Key("badge").Color(Ui.Color.Blue)["Beta"]
            ]);

    private static AttrBag Testid(string value) => new("testid", value);

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
