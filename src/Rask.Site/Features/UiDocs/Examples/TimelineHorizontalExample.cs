namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimelineHorizontalExample : Component
{
    protected override Component? Render() =>
        Ui.Timeline.Horizontal()[
            Ui.TimelineItem.Key("confirmed")[
                Ui.TimelineIndicator[Ui.Icon.Name(Ui.IconName.CreditCard).Micro],
                Ui.TimelineContent[Ui.Heading["Order confirmed"]]
            ],
            Ui.TimelineItem.Key("shipped")[
                Ui.TimelineIndicator[Ui.Icon.Name(Ui.IconName.Truck).Micro],
                Ui.TimelineContent[Ui.Heading["On its way"]]
            ],
            Ui.TimelineItem.Key("delivered")[
                Ui.TimelineIndicator[Ui.Icon.Name(Ui.IconName.Home).Micro],
                Ui.TimelineContent[Ui.Heading["Delivered"]]
            ]
        ];
}
