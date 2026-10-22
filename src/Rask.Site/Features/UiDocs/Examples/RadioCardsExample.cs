namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class RadioCardsExample : Component
{
    private string Shipping { get; set; } = "standard";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.RadioGroup.Bind(() => Shipping).Live().Id("rd-cards").Label("Shipping").Cards.Class("max-sm:flex-col")[
                Ui.Radio.Value("standard").Label("Standard").Description("4-10 business days"),
                Ui.Radio.Value("fast").Label("Fast").Description("2-5 business days"),
                Ui.Radio.Value("next-day").Label("Next day").Description("1 business day")
            ],
            Ui.Text.Data("testid", "ui-radio-cards-state")[$"Shipping {Shipping}."]
        ];
}
