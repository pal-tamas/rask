namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class RadioVerticalCardsExample : Component
{
    private string Shipping { get; set; } = "standard";

    protected override Component? Render() =>
        Ui.RadioGroup.Bind(() => Shipping).Label("Shipping").Cards.Class("max-w-md flex-col")[
            Ui.Radio.Value("standard").Label("Standard").Description("4-10 business days"),
            Ui.Radio.Value("fast").Label("Fast").Description("2-5 business days"),
            Ui.Radio.Value("next-day").Label("Next day").Description("1 business day")
        ];
}
