namespace Rask.Example.Shared.Features;

// Bootstrap cards composed from typed section components (BsCard / BsCardHeader / BsCardBody /
// BsCardTitle / BsCardText). Color tints the whole card via the contrast-aware text-bg-* helper.
public sealed class BsCardsDemo : Component
{
    protected override RenderResult Render() =>
        Div(Class: "row g-3")[
            Div(Class: "col-md-6")[
                BsCard()[
                    BsCardBody()[
                        BsCardTitle()["Card title"],
                        BsCardSubtitle()["Card subtitle"],
                        BsCardText()["Some quick example text to build on the card title and make up the bulk of the card's content."],
                        BsButton(Color: BsColor.Primary)["Go somewhere"]
                    ]
                ]
            ],
            Div(Class: "col-md-6")[
                BsCard(Color: BsColor.Dark)[
                    BsCardHeader()["Featured"],
                    BsCardBody()[
                        BsCardTitle()["Dark card"],
                        BsCardText()["A colored card with a header section."]
                    ],
                    BsCardFooter()["2 days ago"]
                ]
            ]
        ];
}
