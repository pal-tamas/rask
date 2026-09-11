namespace Rask.Site.Features.UiKit;

/// <summary>
///     daisyUI's Data display category, drawn with the kit.
/// </summary>
/// <remarks>
///     Most of this category is static — a badge is a badge. The two that hold state hold it here, in
///     plain fields: which accordion section is open, and whether the standalone collapse is.
/// </remarks>
public sealed partial class UiKitDataDisplayDemo : Component
{
    private string? _section = "ship";
    private bool _advanced;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Section(
            "Accordion",
            "One section at a time, and the page owns which. That is the difference from a run of "
            + "collapses sharing a name: the browser closes the others without telling anyone which won.",
            Div.Data(Testid("ui-accordion"))[
                UiAccordion
                    .Open(_section)
                    .OnOpen(key => { _section = key; })[
                    UiAccordionSection.Key("ship").Title("Shipping").Marker(UiMarker.Arrow)[
                        P["Ships within two working days, tracked."]
                    ],
                    UiAccordionSection.Key("pay").Title("Payment").Marker(UiMarker.Arrow)[
                        P["Card or bank transfer. Invoices on request."]
                    ],
                    UiAccordionSection.Key("returns").Title("Returns").Marker(UiMarker.Arrow)[
                        P["Thirty days, no reason needed."]
                    ]
                ],
                P.Class("mt-2 text-sm text-ui-muted").Data(Testid("ui-accordion-state"))[
                    _section is null ? "All sections closed." : $"Open section: {_section}."
                ]
            ]),

        Section(
            "Collapse",
            "The standalone section. Open is nullable here too — unset lets the browser open it on "
            + "focus, set hands the decision to this page.",
            Div.Data(Testid("ui-collapse"))[
                UiCollapse
                    .Title("Advanced settings")
                    .Marker(UiMarker.Plus)
                    .Open(_advanced)
                    .OnToggle(open => { _advanced = open; })[
                    P["Nothing in here is required."]
                ]
            ]),

        Section(
            "Aura",
            "Decoration, and only decoration — it says nothing a reader who cannot see it would miss. "
            + "Use it on the one thing a surface is steering towards.",
            Div.Data(Testid("ui-aura")).Class("flex flex-wrap gap-6")[
                Aura("holo", UiAuraStyle.Holo, "Holo"),
                Aura("gold", UiAuraStyle.Gold, "Gold"),
                Aura("rainbow", UiAuraStyle.Rainbow, "Rainbow")
            ]),

        Section(
            "Text rotate",
            "One slot of text cycling through several words. Every word is in the markup, so the phrase "
            + "has to make sense with all of them — this rotates a word, it does not rewrite a sentence.",
            P.Class("text-2xl font-semibold tracking-tight").Data(Testid("ui-text-rotate"))[
                "Ship it ",
                UiTextRotate.Words(["fast", "typed", "small", "whole"]).Class("text-primary"),
                "."
            ]),

        Section(
            "Hover 3D",
            "Pointer-only by construction: there is no hover on a touch screen and none from a "
            + "keyboard, so nothing may depend on the tilt.",
            Div.Data(Testid("ui-hover-3d")).Class("max-w-xs")[
                UiHover3d[
                    UiCard.Heading("Tilt me")[P["The content is complete without the effect."]]
                ]
            ]),

        Section(
            "Hover gallery",
            "Several images in the space of one. The first is what shows at rest — and on a touch "
            + "screen it is the only one anybody sees, so put the one that works alone first.",
            Div.Data(Testid("ui-hover-gallery")).Class("max-w-sm")[
                UiHoverGallery.Class("h-48 rounded-xl")[
                    Swatch("one", "bg-primary"),
                    Swatch("two", "bg-secondary"),
                    Swatch("three", "bg-accent")
                ]
            ]),

        Section(
            "Cards, figures and empty states",
            "What an operator screen is made of. A card given an Href is one link, figures and all — so "
            + "nothing inside it may be a button. A mono badge wraps a long token instead of widening its "
            + "row, a code block can say what it holds, and an empty state gives the answer before the reason.",
            Div.Data(Testid("ui-console-pieces"))[
                UiGrid[
                    UiCard
                        .Key("queue")
                        .Href(Routes.UiKitDataGridPage())
                        .Icon(UiIconName.Gear)
                        .Heading("Jobs")
                        .Action(UiStatusDot.Label("2 failed").Tone(UiTone.Error))[
                        UiMetricRow.Columns(2)[
                            UiMetric.Key("outstanding").Label("Outstanding").Value("12"),
                            UiMetric.Key("failed").Label("Failed").Value("2").Tone(UiTone.Error)
                                .Caption("dead after 5 attempts")
                        ]
                    ],
                    UiCard
                        .Key("detail")
                        .Heading("A failed job")
                        .Action(UiBadge.Mono(true)["requestId=0HN8Q2V3R1T0K:00000001"])[
                        UiCode.Content("System.TimeoutException: The SMTP server did not answer in 30 seconds.")
                            .Label("Last error")
                            .Tone(UiTone.Error)
                    ],
                    UiCard.Key("empty")[
                        UiEmpty.Heading("Nothing stored matches")
                            .Detail("Retention drops entries by age and by count.")
                    ]
                ]
            ]),

        Section(
            "The rest of the category",
            "Static, and covered by unit tests for their class composition.",
            Div.Data(Testid("ui-display-rest")).Class("flex flex-wrap items-center gap-3")[
                UiBadge.Key("badge").Tone(UiTone.Info)["Beta"],
                UiKbd.Key("kbd").Text("⌘K").Size(UiSize.Sm),
                UiStatusDot.Key("status").Label("Healthy").Tone(UiTone.Success),
                UiCountdown.Key("countdown").Value(42).Label("seconds left"),
                UiChatBubble.Key("chat").Message("On my way").Author("Ada").When("09:14")
            ])
    ];

    private static Dictionary<string, string?> Testid(string value) => new() { ["testid"] = value };

    private static Component Aura(string key, UiAuraStyle style, string label) =>
        Div.Key(key)[
            UiAura.Style(style).Size(UiSize.Lg)[
                Div.Class("rounded-xl border border-base-300 bg-base-100 px-6 py-4 text-sm font-medium")[
                    label
                ]
            ]
        ];

    private static Component Swatch(string key, string colour) =>
        Div.Key(key).Class($"h-full w-full {colour}");

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
