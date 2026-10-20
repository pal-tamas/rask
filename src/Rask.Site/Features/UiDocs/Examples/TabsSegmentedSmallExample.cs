namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TabsSegmentedSmallExample : Component
{
    protected override Component? Render() =>
        Ui.Tabs.Segmented.Size(Ui.TabsSize.Sm)[Ui.Tab["Demo"], Ui.Tab["Code"]];
}
