namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PhoneSizesExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-4")[
            Ui.Phone.Value("").Sm,
            Ui.Phone.Value("").Xs
        ];
}
