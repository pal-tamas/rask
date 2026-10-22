namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PhoneDisabledExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-4")[
            Ui.Phone.Value("+12015550123").Country("US").Disabled(),
            Ui.Phone.Value("+12015550123").Country("US").ReadOnly()
        ];
}
