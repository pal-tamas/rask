namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PhoneCountriesExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-sm")[
            Ui.Phone.Value("").Countries(["US", "CA", "GB", "AU"])
        ];
}
