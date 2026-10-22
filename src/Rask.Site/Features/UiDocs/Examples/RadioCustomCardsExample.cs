namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class RadioCustomCardsExample : Component
{
    private string Shipping { get; set; } = "standard";

    protected override Component? Render() =>
        Ui.RadioGroup.Bind(() => Shipping).Label("Shipping").Cards.Class("max-sm:flex-col")[
            Ui.Radio.Value("standard")[
                Ui.RadioIndicator,
                Div.Class("flex-1")[
                    Ui.Heading.Class("leading-4")["Standard"],
                    Ui.Text.Sm.Class("mt-2")["4-10 business days"]
                ]
            ],
            Ui.Radio.Value("fast")[
                Ui.RadioIndicator,
                Div.Class("flex-1")[
                    Ui.Heading.Class("leading-4")["Fast"],
                    Ui.Text.Sm.Class("mt-2")["2-5 business days"]
                ]
            ],
            Ui.Radio.Value("next-day")[
                Ui.RadioIndicator,
                Div.Class("flex-1")[
                    Ui.Heading.Class("leading-4")["Next day"],
                    Ui.Text.Sm.Class("mt-2")["1 business day"]
                ]
            ]
        ];
}
