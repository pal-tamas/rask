namespace Rask.Example.Shared.Features;

// Bootstrap placeholders (loading skeletons) — sized with Col (the col-{n} grid width), optionally tinted
// with Color and scaled with Size, and shimmering via Animation (Glow / Wave). The left card reads as a
// card that is still loading; the right column shows the coloured, sized, wave-animated variants.
public sealed partial class BsPlaceholderDemo : Component
{
    protected override Component? Render() =>
        BsRow(Gutter: 3)[
            BsCol(Md: 6)[
                BsCard()[
                    BsCardBody()[
                        BsCardTitle()[BsPlaceholder(Col: 6, Animation: BsPlaceholderAnimation.Glow)],
                        BsCardText()[
                            BsPlaceholder(Col: 7, Animation: BsPlaceholderAnimation.Glow),
                            BsPlaceholder(Col: 4, Animation: BsPlaceholderAnimation.Glow),
                            BsPlaceholder(Col: 4, Animation: BsPlaceholderAnimation.Glow),
                            BsPlaceholder(Col: 6, Animation: BsPlaceholderAnimation.Glow),
                            BsPlaceholder(Col: 8, Animation: BsPlaceholderAnimation.Glow)
                        ]
                    ]
                ]
            ],
            BsCol(Md: 6, Class: Bs.Join(Display.Flex(), Flex.Column(), Flex.Gap(2)))[
                BsPlaceholder(Col: 12, Color: BsColor.Primary, Animation: BsPlaceholderAnimation.Wave),
                BsPlaceholder(Col: 8, Color: BsColor.Success, Size: BsSize.Lg, Animation: BsPlaceholderAnimation.Wave),
                BsPlaceholder(Col: 6, Color: BsColor.Danger, Size: BsSize.Sm, Animation: BsPlaceholderAnimation.Wave)
            ]
        ];
}
