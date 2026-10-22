namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PhoneCountryOrderExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-sm")[
            Ui.Phone.Value("").CountryOrder(["US", "CA", "GB", "AU"])
        ];
}
