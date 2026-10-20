namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TabsSegmentedIconsExample : Component
{
    protected override Component? Render() =>
        Ui.Tabs.Segmented[
            Ui.Tab.Icon(Ui.IconName.ListBullet)["List"],
            Ui.Tab.Icon(Ui.IconName.Squares2x2)["Board"],
            Ui.Tab.Icon(Ui.IconName.CalendarDays)["Timeline"]
        ];
}
