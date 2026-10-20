namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TabsDynamicExample : Component
{
    private readonly List<string> _tabs = ["Tab #1", "Tab #2"];

    protected override Component? Render() =>
        Ui.TabGroup[
            Ui.Tabs[
                _tabs.Select(tab => Ui.Tab.Key(tab).Name(tab)[tab]),
                Ui.Tab.Key("add").Icon(Ui.IconName.Plus).Action().OnClick(() => _tabs.Add($"Tab #{_tabs.Count + 1}"))["Add tab"]
            ],
            _tabs.Select(tab => Ui.TabPanel.Key("panel " + tab).Name(tab).Class("pt-4! text-sm")[$"What {tab} holds."])
        ];
}
