namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TabsPillsExample : Component
{
    protected override Component? Render() =>
        Ui.Tabs.Pills[Ui.Tab["List"], Ui.Tab["Board"], Ui.Tab["Timeline"]];
}
