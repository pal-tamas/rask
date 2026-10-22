namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class RadioCardIconsExample : Component
{
    private string Shipping { get; set; } = "standard";

    protected override Component? Render() =>
        Ui.RadioGroup.Bind(() => Shipping).Label("Shipping").Cards.Class("max-sm:flex-col")[
            Ui.Radio.Value("standard").Icon(Ui.IconName.Truck).Label("Standard").Description("4-10 business days"),
            Ui.Radio.Value("fast").Icon(Ui.IconName.Cube).Label("Fast").Description("2-5 business days"),
            Ui.Radio.Value("next-day").Icon(Ui.IconName.Clock).Label("Next day").Description("1 business day")
        ];
}
