namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CardVariantsExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid gap-4 sm:grid-cols-3")[
            Ui.Card.Sm[Ui.CardHeading["Default"], Ui.Text.Class("mt-1")["Raised, for primary content."]],
            Ui.Card.Sm.Muted[Ui.CardHeading["Muted"], Ui.Text.Class("mt-1")["A quieter tint for secondary panels."]],
            Ui.Card.Sm.Soft[Ui.CardHeading["Soft"], Ui.Text.Class("mt-1")["The faintest tint, for light grouping."]],
            Ui.Card.Sm.Outline[Ui.CardHeading["Outline"], Ui.Text.Class("mt-1")["Just an edge, over whatever's behind it."]],
            Ui.Card.Sm.Filled[Ui.CardHeading["Filled"], Ui.Text.Class("mt-1")["A tint with no edge, for inline surfaces."]]
        ];
}
