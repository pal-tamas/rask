namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FieldFieldsetExample : Component
{
    private string? _country = "United States";

    protected override Component? Render() =>
        Ui.Fieldset[
            Ui.Legend["Shipping address"],
            Div.Class("space-y-6")[
                Ui.Input.Of<string>().Label("Street address line 1").Placeholder("123 Main St").Class("max-w-sm"),
                Ui.Input.Of<string>().Label("Street address line 2").Placeholder("Apartment, studio, or floor").Class("max-w-sm"),
                Div.Class("grid grid-cols-2 gap-x-4 gap-y-6")[
                    Ui.Input.Of<string>().Label("City").Placeholder("San Francisco"),
                    Ui.Input.Of<string>().Label("State / Province").Placeholder("CA"),
                    Ui.Input.Of<string>().Label("Postal / Zip code").Placeholder("12345"),
                    Ui.Select.Value(_country).OnChange(country => { _country = country; }).Label("Country")[
                        Ui.SelectOption["United States"],
                        Ui.SelectOption["Canada"],
                        Ui.SelectOption["Mexico"]
                    ]
                ]
            ]
        ];
}
