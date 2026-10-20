namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CardExample : Component
{
    protected override Component? Render() =>
        Ui.Card.Class("max-w-sm")[
            Ui.CardHeading.Lg["Are you sure?"],
            Ui.Text.Class("mt-2 mb-4")["Your post will be deleted permanently. This action cannot be undone."],
            Ui.Button["Delete"]
        ];
}
