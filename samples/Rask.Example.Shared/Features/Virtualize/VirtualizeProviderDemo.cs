using Rask.Core.Virtualization;

namespace Rask.Example.Shared.Features;

// The same component, now backed by an ItemsProvider that simulates a 350 ms API call per
// window. Visible rows show a "—" placeholder until the fetch resolves, then morph in.
// Navigating away mid-fetch cancels the in-flight call: VirtualizeModel cancels its
// CancellationTokenSource in OnUnmount (and supersedes it whenever a new viewport arrives),
// so honour req.CancellationToken in your own providers to let the cancellation propagate.
public sealed class VirtualizeProviderDemo : Component
{
    // Sticky header on the <th> cells — see VirtualizeItemsDemo for why this beats a sticky <thead>.
    private const string StickyHead =
        "position:sticky; top:0; z-index:1; background:#f8f9fa; box-shadow:inset 0 -1px 0 #dee2e6; ";

    protected override RenderResult Render() =>
        VirtualizeModel(
            ctx => Div(
                Class: "border rounded bg-white",
                Style: "height:360px; overflow:auto;",
                Data: new Dictionary<string, string?> { ["testid"] = "virtualize-async-scroller" },
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
                                Data: new Dictionary<string, string?>
                                {
                                    ["row-index"] = item.Index.ToString(),
                                    ["rask-key"] = item.Index.ToString(),
                                    ["placeholder"] = item.IsPlaceholder ? "true" : null
                                })[
                                Td()[item.IsPlaceholder ? "—" : item.Value!.Index],
                                Td()[item.IsPlaceholder ? "—" : item.Value!.Name],
                                Td()[item.IsPlaceholder ? "—" : item.Value!.City],
                                Td(Style: "text-align:right;")[
                                    item.IsPlaceholder ? "—" : item.Value!.Balance.ToString("0.00")]
                            ])
                    ]
                ],
                Div(Style: $"height:{ctx.OffsetAfter}px")
            ],
            ItemsProvider: FetchRowsAsync,
            ItemSize: 32,
            OverscanCount: 4,
            InitialClientHeight: 360);

    private static async ValueTask<ItemsProviderResult<VirtualizeRow>> FetchRowsAsync(ItemsProviderRequest req)
    {
        // The token is honoured so navigating away from /virtualize while a fetch is in flight
        // unwinds the Task.Delay promptly. Without the token check the delay would complete and
        // the continuation would try to update the cache after the component was disposed.
        await Task.Delay(350, req.CancellationToken).ConfigureAwait(false);
        var rows = VirtualizeData.Rows;
        var count = Math.Min(req.Count, rows.Length - req.StartIndex);
        var slice = new VirtualizeRow[Math.Max(count, 0)];
        for (var i = 0; i < slice.Length; i++)
        {
            slice[i] = rows[req.StartIndex + i];
        }

        return new ItemsProviderResult<VirtualizeRow>(slice, rows.Length);
    }
}
