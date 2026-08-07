namespace Rask.Example.Shared.Features;

// Bootstrap buttons, button groups and badges — Color/Size/Outline are typed enums (BsColor/BsSize),
// not class strings. Each BsButton wraps the core Button and emits the right .btn classes.
public sealed partial class BsButtonsDemo : Component
{
    protected override Component? Render() =>
    [
        Div(Class: "vstack gap-3")[
            Div(Class: "hstack gap-2 flex-wrap")[
                BsButton(Color: BsColor.Primary)["Primary"],
                BsButton(Color: BsColor.Secondary)["Secondary"],
                BsButton(Color: BsColor.Success)["Success"],
                BsButton(Color: BsColor.Danger)["Danger"],
                BsButton(Color: BsColor.Warning)["Warning"],
                BsButton(Color: BsColor.Info)["Info"]
            ],
            Div(Class: "hstack gap-2 flex-wrap")[
                BsButton(Color: BsColor.Primary, Outline: true)["Outline"],
                BsButton(Color: BsColor.Primary, Size: BsSize.Sm)["Small"],
                BsButton(Color: BsColor.Primary, Size: BsSize.Lg)["Large"],
                BsButton(Color: BsColor.Primary, Disabled: true)["Disabled"]
            ],
            BsButtonGroup()[
                BsButton(Color: BsColor.Primary)["Left"],
                BsButton(Color: BsColor.Primary)["Middle"],
                BsButton(Color: BsColor.Primary)["Right"]
            ],
            Div(Class: "hstack gap-2 align-items-center")[
                BsBadge(Color: BsColor.Primary)["Badge"],
                BsBadge(Color: BsColor.Success, Pill: true)["Pill"],
                BsButton(Color: BsColor.Primary)["Inbox ", BsBadge(Color: BsColor.Light)["4"]]
            ]
        ]
    ];
}
