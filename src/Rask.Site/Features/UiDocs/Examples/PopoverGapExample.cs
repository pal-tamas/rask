namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PopoverGapExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid max-w-sm grid-cols-2 gap-4")[
            Ui.Dropdown.Key("gap").Gap(16)[
                Ui.Button.Filled.Sm.Class("w-full")["Gap: 16px"],
                Ui.Popover[Ui.Text["16px from its button."]]
            ],
            Ui.Dropdown.Key("offset").Offset(32)[
                Ui.Button.Filled.Sm.Class("w-full")["Offset: 32px"],
                Ui.Popover[Ui.Text["Shifted 32px along its button."]]
            ]
        ];
}
