namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PopoverPositionExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid max-w-xs grid-cols-2 gap-4")[
            Ui.Dropdown.Key("ts").Top.Start[
                Ui.Button.Filled.Sm.Class("w-full")["Top Start"],
                Ui.Popover[Ui.Text["Above, at the start of its button."]]
            ],
            Ui.Dropdown.Key("rc").Right.Center[
                Ui.Button.Filled.Sm.Class("w-full")["Right Center"],
                Ui.Popover[Ui.Text["To the right, centred on its button."]]
            ],
            Ui.Dropdown.Key("lc").Left.Center[
                Ui.Button.Filled.Sm.Class("w-full")["Left Center"],
                Ui.Popover[Ui.Text["To the left, centred on its button."]]
            ],
            Ui.Dropdown.Key("be").Bottom.End[
                Ui.Button.Filled.Sm.Class("w-full")["Bottom End"],
                Ui.Popover[Ui.Text["Below, at the end of its button."]]
            ]
        ];
}
