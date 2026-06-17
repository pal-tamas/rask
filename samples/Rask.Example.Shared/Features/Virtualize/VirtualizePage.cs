using Rask.Core.Routing;
using Rask.Core.Virtualization;

namespace Rask.Example.Shared.Features;

[Route("virtualize")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class VirtualizePage : Component
{
    private static readonly Row[] _rows = BuildRows(10_000);

    protected override RenderResult Head => Title()["Virtualize — Rask"];

    private static Row[] BuildRows(int count)
    {
        var firsts = new[]
        {
            "Ada", "Grace", "Linus", "Margaret", "Donald", "Barbara", "Edsger", "Tony", "Alan", "John"
        };
        var lasts = new[]
        {
            "Lovelace", "Hopper", "Torvalds", "Hamilton", "Knuth", "Liskov", "Dijkstra", "Hoare", "Turing", "Backus"
        };
        var cities = new[]
        {
            "London", "New York", "Helsinki", "Boston", "Stanford", "Cambridge", "Amsterdam", "Oxford",
            "Manchester", "Berkeley"
        };
        var rng = new Random(42);
        var rows = new Row[count];
        for (var i = 0; i < count; i++)
        {
            var name = $"{firsts[i % firsts.Length]} {lasts[i / firsts.Length % lasts.Length]} #{i + 1:D5}";
            rows[i] = new Row(i + 1, name, cities[rng.Next(cities.Length)], rng.Next(0, 100000) / 100m);
        }

        return rows;
    }

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Virtualize",
            $"Headless virtualization. The list below contains {_rows.Length:N0} rows, but the DOM only holds the visible window plus a small overscan."),
        P(Class: "small text-secondary mb-4")[
            "Scroll the box below. The two spacer divs (",
            Code()["OffsetBefore"], " and ", Code()["OffsetAfter"],
            ") keep the scrollbar consistent with the full row count while ",
            Code()["VisibleItems"], " only emits the rows currently on screen."
        ],
        VirtualizeModel<Row>(
            ctx => Div(
                Class: "border rounded bg-white",
                Style: "height:400px; overflow:auto;",
                Data: new Dictionary<string, string?> { ["testid"] = "virtualize-scroller" },
                OnScroll: ctx.OnScroll)[
                Div(Style: $"height:{ctx.OffsetBefore}px"),
                Table(Class: "table table-sm mb-0", Style: "table-layout:fixed; width:100%;")[
                    Thead(Style: "position:sticky; top:0; background:#f8f9fa; z-index:1;")[
                        Tr()[
                            Th(Style: "width:80px;")["#"],
                            Th()["Name"],
                            Th(Style: "width:140px;")["City"],
                            Th(Style: "width:120px; text-align:right;")["Balance"]
                        ]
                    ],
                    Tbody()[
                        ctx.VisibleItems.Select(item =>
                            Tr(
                                Style: $"height:{ctx.ItemSize}px;",
                                // data-rask-key engages the morph algorithm's keyed
                                // reconciliation path: scrolling the window moves the
                                // existing <tr> nodes instead of replacing them, so
                                // focus and scroll state survive the re-render.
                                Data: new Dictionary<string, string?>
                                {
                                    ["row-index"] = item.Index.ToString(), ["rask-key"] = item.Index.ToString()
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
            _rows,
            ItemSize: 32,
            OverscanCount: 4,
            InitialClientHeight: 400),
        P(Class: "small text-secondary mt-3 mb-0")[
            Code()["data-row-index"],
            " on each row lets you eyeball which slice is rendered. Open DevTools and inspect — only ~",
            Strong()["20–30"], " rows live in the DOM at any time."
        ],
        H2(Class: "h4 mt-5 mb-3")["Async paging via ItemsProvider"],
        P(Class: "small text-secondary mb-3")[
            "The same component, now backed by a provider that simulates a 350 ms API call per window. ",
            "Visible rows show a placeholder ", Code()["—"],
            " until the fetch resolves, then morph in. Navigating away mid-fetch cancels the in-flight ",
            "call: Virtualize cancels its ", Code()["CancellationTokenSource"], " in ",
            Code()["OnUnmount"], " (and supersedes it whenever a new viewport request arrives). ",
            "Honour ", Code()["req.CancellationToken"],
            " in your own providers so the cancellation actually propagates."
        ],
        VirtualizeModel(
            ctx => Div(
                Class: "border rounded bg-white",
                Style: "height:400px; overflow:auto;",
                Data: new Dictionary<string, string?> { ["testid"] = "virtualize-async-scroller" },
                OnScroll: ctx.OnScroll)[
                Div(Style: $"height:{ctx.OffsetBefore}px"),
                Table(Class: "table table-sm mb-0", Style: "table-layout:fixed; width:100%;")[
                    Thead(Style: "position:sticky; top:0; background:#f8f9fa; z-index:1;")[
                        Tr()[
                            Th(Style: "width:80px;")["#"],
                            Th()["Name"],
                            Th(Style: "width:140px;")["City"],
                            Th(Style: "width:120px; text-align:right;")["Balance"]
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
            InitialClientHeight: 400)
    ];

    private static async ValueTask<ItemsProviderResult<Row>> FetchRowsAsync(ItemsProviderRequest req)
    {
        // Simulate an API call so the placeholder window is observable. The token is honoured
        // so navigating away from /virtualize while a fetch is in flight unwinds the Task.Delay
        // promptly — Virtualize cancels its CancellationTokenSource in OnUnmount, and supersedes
        // the prior CTS whenever a new viewport request arrives. Without the token check, the
        // delay would complete and the continuation would try to update _cache after disposal.
        await Task.Delay(350, req.CancellationToken).ConfigureAwait(false);
        var count = Math.Min(req.Count, _rows.Length - req.StartIndex);
        var slice = new Row[Math.Max(count, 0)];
        for (var i = 0; i < slice.Length; i++)
        {
            slice[i] = _rows[req.StartIndex + i];
        }

        return new ItemsProviderResult<Row>(slice, _rows.Length);
    }

    private sealed record Row(int Index, string Name, string City, decimal Balance);
}
