using Rask.Core.Virtualization;

namespace Rask.Example.Shared.Features;

// In-memory virtualization: 10,000 rows in VirtualizeData.Rows, but only the visible window
// (plus a small overscan) ever reaches the DOM. The off-screen rows above and below are reserved
// by two spacer *rows* inside the tbody, so the single table is the scroller's only child.
public sealed class VirtualizeItemsDemo : Component
{
    // The header stays put while scrolling because (a) the table is the scroller's only child and
    // its total height is constant — the spacer rows just redistribute height inside tbody as the
    // window moves — so the sticky header's containing block never resizes, and (b) sticky lives on
    // the <th> cells (not <thead>), each painting an opaque background with an inset box-shadow
    // divider. Earlier the spacers were divs *outside* the table: the table's own box was relaid out
    // every scroll frame and the header unstuck (vanished mid-scroll, snapped back on stop).
    private const string StickyHead =
        "position:sticky; top:0; z-index:1; background:#f8f9fa; box-shadow:inset 0 -1px 0 #dee2e6; ";

    protected override Component? Render() =>
        VirtualizeModel<VirtualizeRow>(
            ctx => Div(
                Class: "border rounded bg-white",
                Style: "height:360px; overflow:auto;",
                Data: new Dictionary<string, string?> { ["testid"] = "virtualize-scroller" },
                OnScroll: ctx.OnScroll)[
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
                    Tbody()[BodyRows(ctx)]
                ]
            ],
            VirtualizeData.Rows,
            ItemSize: 32,
            OverscanCount: 4,
            InitialClientHeight: 360);

    // tbody = top spacer + the visible window + bottom spacer. Every child carries a stable
    // data-rask-key so the whole tbody stays on the keyed reconciliation path (it's all-or-nothing):
    // the spacers keep their identity and only their height attribute is patched as you scroll, while
    // the real rows move by index — all trusted diff ops, so the header is never re-rendered.
    private static IEnumerable<Component> BodyRows(VirtualizationContext<VirtualizeRow> ctx)
    {
        yield return Spacer(ctx.OffsetBefore, "spacer-before");

        foreach (var item in ctx.VisibleItems)
        {
            yield return Tr(
                Style: $"height:{ctx.ItemSize}px;",
                Data: new Dictionary<string, string?>
                {
                    ["row-index"] = item.Index.ToString(),
                    ["rask-key"] = item.Index.ToString()
                })[
                Td()[item.Value?.Index.ToString() ?? ""],
                Td()[item.Value?.Name ?? ""],
                Td()[item.Value?.City ?? ""],
                Td(Style: "text-align:right;")[item.Value?.Balance.ToString("0.00") ?? ""]
            ];
        }

        yield return Spacer(ctx.OffsetAfter, "spacer-after");
    }

    private static Component Spacer(int height, string key) =>
        Tr(
            Style: $"height:{height}px;",
            Data: new Dictionary<string, string?> { ["rask-key"] = key })[
            Td(Colspan: 4)
        ];
}
