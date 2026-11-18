namespace Rask.Site.Features.UiDocs.Examples;

// A record's summary: each fact a label over its value, as many to a row as the card has room for, one on a phone.
public sealed partial class CardFactsExample : Component
{
    protected override Component? Render() =>
        Ui.Card[
            Ui.Facts.Id("ui-driver-facts")[
                Ui.Fact("Driver", "Lindsey Aminoff"),
                Ui.Fact("Company", "Aminoff Freight & Haulage"),
                Ui.Fact("Status", Ui.Badge.Sm.Color(Ui.Color.Lime)["Active"]),
                Ui.Fact("Driver card", null)
            ]
        ];
}
