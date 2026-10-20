namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PopoverHoverExample : Component
{
    protected override Component? Render() =>
        Ui.Dropdown.Hover().Bottom.Start.Offset(-16).Gap(10)[
            Button.Type(ButtonType.Button).Class("flex items-center gap-3")[
                Ui.Avatar.Sm.Name("Caleb Porzio"),
                Ui.Heading["Caleb Porzio"]
            ],
            Ui.Popover.Class("flex flex-col gap-3 rounded-xl shadow-xl")[
                Ui.Avatar.Xl.Name("Caleb Porzio"),
                Div[
                    Ui.Heading.Lg["Caleb Porzio"],
                    Div.Class("flex items-center gap-2")[Ui.Text.Lg["@calebporzio"], Ui.Badge["Follows you"]]
                ],
                Div.Class("flex items-center gap-4")[
                    Div.Class("flex gap-1")[Ui.Heading["775"], Ui.Text["following"]],
                    Div.Class("flex gap-1")[Ui.Heading["50.2k"], Ui.Text["followers"]]
                ],
                Div.Class("flex gap-2")[
                    Ui.Button.Sm.Icon(Ui.IconName.Check).Class("flex-1")["Following"],
                    Ui.Button.Primary.Sm.Icon(Ui.IconName.ChatBubbleLeftRight).Class("flex-1")["Message"]
                ]
            ]
        ];
}
