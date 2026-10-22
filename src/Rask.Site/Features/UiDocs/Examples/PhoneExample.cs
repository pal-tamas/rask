namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PhoneExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-sm")[
            Ui.Phone.Value("").Country("US").Label("Phone number")
        ];
}
