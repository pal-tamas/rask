namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PhoneInvalidExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-sm")[
            Ui.Phone.Value("").Invalid()
        ];
}
