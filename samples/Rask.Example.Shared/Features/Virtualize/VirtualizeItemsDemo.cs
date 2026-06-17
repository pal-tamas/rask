using Rask.Core.Virtualization;

namespace Rask.Example.Shared.Features;

// In-memory virtualization: 10,000 rows in VirtualizeData.Rows, but only the visible window
// (plus a small overscan) ever reaches the DOM. The two spacer divs reserve the scroll height
// for the off-screen rows so the scrollbar stays proportional to the full list.
public sealed class VirtualizeItemsDemo : Component
{
    // The sticky header lives on the <th> cells, NOT the <thead>: position:sticky on thead/tr is
    // unevenly supported across engines and visibly flickers as the windowed rows re-render under
    // it on every scroll. Each cell instead paints an opaque background and draws its bottom
    // divider with an inset box-shadow, and the table opts into border-collapse:separate so that
    // divider doesn't scroll away with the collapsed border model. Result: a rock-steady header.
    private const string StickyHead =
        "position:sticky; top:0; z-index:1; background:#f8f9fa; box-shadow:inset 0 -1px 0 #dee2e6; ";

    protected override RenderResult Render() =>
        VirtualizeModel<VirtualizeRow>(
            ctx => Div(
                Class: "border rounded bg-white",
                Style: "height:360px; overflow:auto;",
                Data: new Dictionary<string, string?> { ["testid"] = "virtualize-scroller" },
                OnScroll: ctx.OnScroll)[
                Div(Style: $"height:{ctx.OffsetBefore}px"),
                Table(
                    Class: "table table-sm mb-0",
                    Style: "table-layout:fixed; width:100%; border-collapse:separate; border-spacing:0;")[
                    Thead()[
                        Tr()[
                            Th(Style: StickyHead + "width:64px;")["#"],
                            Th(Style: StickyHead)["Name"],
                            Th(Style: StickyHead + "width:120px;")["City"],
                            Th(Style: StickyHead + "width:110px; text-align:right;")["Balance"]
                        ]
                    ],
                    Tbody()[
                        ctx.VisibleItems.Select(item =>
                            Tr(
                                Style: $"height:{ctx.ItemSize}px;",
                                // data-rask-key engages the morph algorithm's keyed reconciliation
                                // path: scrolling the window moves the existing <tr> nodes instead of
                                // replacing them, so focus and scroll state survive the re-render.
                                Data: new Dictionary<string, string?>
                                {
                                    ["row-index"] = item.Index.ToString(),
                                    ["rask-key"] = item.Index.ToString()
                                })[
                                Td()[item.Value?.Index.ToString() ?? ""],
                                Td()[item.Value?.Name ?? ""],
                                Td()[item.Value?.City ?? ""],
                                Td(Style: "text-align:right;")[item.Value?.Balance.ToString("0.00") ?? ""]
                            ])
                    ]
                ],
                Div(Style: $"height:{ctx.OffsetAfter}px")
            ],
            VirtualizeData.Rows,
            ItemSize: 32,
            OverscanCount: 4,
            InitialClientHeight: 360);
}
