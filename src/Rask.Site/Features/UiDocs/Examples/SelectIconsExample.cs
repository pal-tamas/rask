namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectIconsExample : Component
{
    private string? _method;

    protected override Component? Render() =>
        Ui.Select.Value(_method).OnChange(value => { _method = value; }).Listbox.Placeholder("Choose method...").Class("max-w-sm")[
            Ui.SelectOption.Value("card").Label("Credit card").Icon(Ui.IconName.CreditCard),
            Ui.SelectOption.Value("paypal").Label("PayPal").Icon(Ui.IconName.Banknotes),
            Ui.SelectOption.Value("bank").Label("Bank transfer").Icon(Ui.IconName.BuildingLibrary)
        ];
}
