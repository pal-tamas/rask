namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CardLinkExample : Component
{
    protected override Component? Render() =>
        A.Href("#").Aria("label", "Latest on our blog").Class("block max-w-sm")[
            Ui.Card.Xs.Class("hover:bg-zinc-50 dark:hover:bg-zinc-700")[
                Ui.CardHeading["Latest on our blog"],
                Ui.Text.Class("mt-2")["Stay up to date with our latest insights, tutorials, and product updates."]
            ]
        ];
}
