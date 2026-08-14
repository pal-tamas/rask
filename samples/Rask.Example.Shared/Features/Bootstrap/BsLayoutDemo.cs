namespace Rask.Example.Shared.Features;

// The layout primitives from Rask.Bootstrap: BsContainer (page wrapper), BsRow + BsCol (the 12-unit
// responsive grid), and BsStack (a flex row/column with a gap). Resize the window to watch the grid and
// the responsive stack reflow at the md breakpoint.
public sealed partial class BsLayoutDemo : Component
{
    private static Component Tile(string label) =>
        Div
            .Class(Bs.Join(Bg.BodyTertiary, Border.All, Rounded.Default, Padding.All(2), Txt.Center(),
            Font.Small))[label];

    private static new Component Section(string title, Component body) =>
        Div[
            H6.Class(Bs.Join(Txt.Uppercase, Txt.Muted, Font.Bold, Margin.Bottom(2)))[title],
            body
        ];

    protected override Component? Render() =>
        BsStack.Vertical(true).Gap(4)[

            // Equal-width columns: no span at all, so each BsCol shares the row evenly.
            Section("BsRow + BsCol — equal width", BsRow.Gutter(2)[
                BsCol[Tile("BsCol()")],
                BsCol[Tile("BsCol()")],
                BsCol[Tile("BsCol()")]
            ]),

            // Spans in the 12-unit grid, plus Auto sizing to its content.
            Section("Spans & auto", BsRow.Gutter(2)[
                BsCol.Span(8)[Tile("Span: 8")],
                BsCol.Span(4)[Tile("Span: 4")],
                BsCol.Auto(true)[Tile("Auto: true")],
                BsCol[Tile("BsCol() fills the rest")]
            ]),

            // Stacked breakpoints: full width on a phone, halves from md, thirds from lg. The id is the
            // E2E's handle — the reflow is a real CSS media query, so only a browser can prove it.
            Section("Responsive spans (resize me)", BsRow.Gutter(2).Id("bs-layout-responsive")[
                BsCol.Md(6).Lg(4)[Tile("Md: 6, Lg: 4")],
                BsCol.Md(6).Lg(4)[Tile("Md: 6, Lg: 4")],
                BsCol.Md(12).Lg(4)[Tile("Md: 12, Lg: 4")]
            ]),

            Section("BsStack — horizontal, vertical, wrapping", BsRow.Gutter(3)[
                BsCol.Md(6)[
                    BsStack.Gap(2).WrapItems(true)[
                        BsBadge.Color(BsColor.Primary)["Gap: 2"],
                        BsBadge.Color(BsColor.Secondary)["WrapItems: true"],
                        BsBadge.Color(BsColor.Success)["horizontal by default"]
                    ]
                ],
                BsCol.Md(6)[
                    BsStack.Vertical(true).Gap(2)[
                        Tile("Vertical: true"),
                        Tile("Gap: 2")
                    ]
                ]
            ]),

            // Align is the cross axis, Justify the main one. Bootstrap's .hstack bakes in
            // align-items-center; BsStack makes it an explicit opt-in instead.
            Section("Align & justify", BsStack.Vertical(true).Gap(2)[
                // WrapItems here is not decoration: without it this row overflows its container on a
                // phone. Each wrapped line still centres against its own tallest item.
                BsStack
                    .Gap(2)
                    .Align(BsAlign.Center)
                    .WrapItems(true)
                    .Class(Bs.Join(Border.All, Rounded.Default, Padding.All(2)))[
                    BsBadge.Color(BsColor.Info)["Align: Center"],
                    Div.Class(Bs.Join(Padding.Y(3), Bg.BodyTertiary, Padding.X(2), Font.Small))["taller item"],
                    BsBadge.Color(BsColor.Info)["centred on the cross axis"]
                ],
                BsStack
                    .Justify(BsJustify.Between)
                    .Align(BsAlign.Center)
                    .Class(Bs.Join(Border.All, Rounded.Default, Padding.All(2)))[
                    Span.Class(Font.Small)["Justify: Between"],
                    BsButton.Color(BsColor.Primary).Size(BsSize.Sm)["pushed to the end"]
                ]
            ]),

            // Bootstrap ships no responsive variant of .vstack/.hstack — this composes only because
            // BsStack is built on d-flex.
            Section("Responsive direction", BsStack.Vertical(true).Gap(2).Class(Flex.Row(Bp.Md))[
                Tile("Class: Flex.Row(Bp.Md)"),
                Tile("column below md"),
                Tile("row from md up")
            ]),

            // BsContainer is the page wrapper — shown as a nested example here since this demo already
            // renders inside the showcase's own container.
            Section("BsContainer",
                BsContainer.Class(Bs.Join(Bg.BodyTertiary, Border.All, Rounded.Default, Padding.All(3)))[
                    P.Class(Bs.Join(Font.Small, Margin.Bottom(0)))[
                        "BsContainer() centres and caps the page width; Fluid: true spans it; "
                        + "FluidBelow: Bp.Md is fluid below md and capped from md up."
                    ]
                ])
        ];
}
