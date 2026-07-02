namespace Rask.Example.Shared.Features;

// Bootstrap Icons via the typed BsIconName enum — every glyph is compile-checked (no string typos),
// and Color tints it. Icons are decorative (aria-hidden) unless you pass AriaLabel.
public sealed class BsIconsDemo : Component
{
    protected override Component? Render() =>
    [
        Div(Class: "vstack gap-3")[
            Div(Class: "fs-2 hstack gap-3 flex-wrap")[
                BsIcon(Name: BsIconName.HouseDoor),
                BsIcon(Name: BsIconName.HeartFill, Color: BsColor.Danger),
                BsIcon(Name: BsIconName.StarFill, Color: BsColor.Warning),
                BsIcon(Name: BsIconName.Github),
                BsIcon(Name: BsIconName.Check2Circle, Color: BsColor.Success),
                BsIcon(Name: BsIconName.Bell, Color: BsColor.Primary)
            ],
            Div(Class: "hstack gap-2")[
                BsButton(Color: BsColor.Primary)[BsIcon(Name: BsIconName.Download, Class: "me-1"), "Download"],
                BsButton(Color: BsColor.Success, Outline: true)[BsIcon(Name: BsIconName.ArrowRight, AriaLabel: "Next")]
            ]
        ]
    ];
}
