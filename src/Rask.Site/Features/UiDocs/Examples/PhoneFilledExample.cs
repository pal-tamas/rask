namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PhoneFilledExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-sm")[
            Ui.Phone.Value("").Filled
        ];
}
