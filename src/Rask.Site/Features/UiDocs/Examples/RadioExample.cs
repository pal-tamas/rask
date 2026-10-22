namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class RadioExample : Component
{
    private string Payment { get; set; } = "cc";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.RadioGroup.Bind(() => Payment).Live().Id("rd-payment").Label("Select your payment method")[
                Ui.Radio.Value("cc").Label("Credit Card"),
                Ui.Radio.Value("paypal").Label("Paypal"),
                Ui.Radio.Value("ach").Label("Bank transfer")
            ],
            Ui.Text.Data("testid", "ui-radio-state")[$"Paying by {Payment}."]
        ];
}
