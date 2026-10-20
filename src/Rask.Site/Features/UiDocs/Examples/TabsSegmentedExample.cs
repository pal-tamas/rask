namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TabsSegmentedExample : Component
{
    private string View { get; set; } = "list";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Tabs.Segmented.Bind(() => View)[
                Ui.Tab.Name("list")["List"],
                Ui.Tab.Name("board")["Board"],
                Ui.Tab.Name("timeline")["Timeline"]
            ],
            Ui.Button.Sm.Ghost.OnClick(() => View = "list")["Back to the list"]
        ];
}
