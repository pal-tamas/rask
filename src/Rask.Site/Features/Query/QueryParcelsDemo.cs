using Rask.Core.Routing;
using Rask.Query;

namespace Rask.Site.Features;

// Rask.Query's four shapes on one small page, none of which loads anything by hand:
//   1. a query declared in Render that follows the URL's ?page=, keeping the old page on screen while the next loads;
//   2. a dependent query, paused until a parcel is picked;
//   3. a function query over a local source, keyed QueryKey.For<Parcel>(input);
//   4. a command per row whose IsPending disables its own button, and whose success refetches the queries it names.
public sealed partial class QueryParcelsDemo(Navigator nav, RouteState route, ParcelStore store) : Component
{
    private static readonly QueryOptions KeepPrevious = new() { KeepPreviousData = true };

    private int? _picked;
    private string _contents = "book";

    // 2. Dependent: the lambda returns null until a parcel is picked, and a null message pauses the query.
    private Query<Parcel?> Picked => field ??= QueryClient.Query(() => _picked is { } id ? new GetParcel(id) : null);

    // 3. A function query: not a CQRS message, just a key and a fetch. The input is read again on each render.
    private Query<IReadOnlyList<Parcel>> Search => field ??= QueryClient.Query(
        QueryKey.For<Parcel>(), () => _contents, (contents, ct) => store.SearchAsync(contents, ct));

    // A demo is not a routed page, so it reads ?page= from the route rather than through [QueryParam].
    private int PageNumber =>
        route.Query.TryGetValue("page", out var values) && int.TryParse(values.FirstOrDefault(), out var page) && page > 0
            ? page
            : 1;

    protected override Component? Render()
    {
        // 1. Declared in Render: the same call hands back the same query each render, re-pointed at the new page.
        var parcels = QueryClient.Query(new GetParcelPage(PageNumber), KeepPrevious);
        var pages = parcels.Data?.Pages ?? 1;

        return Div.Id("query-demo").Class("flex flex-col gap-4")[
            Div.Class("flex gap-2 items-center flex-wrap")[
                Ui.Button.Id("query-prev").Disabled(PageNumber <= 1).OnClick(() => GoTo(PageNumber - 1))["Previous"],
                Span.Id("query-page")[$"Page {PageNumber} of {pages}"],
                Ui.Button.Id("query-next").Disabled(PageNumber >= pages).OnClick(() => GoTo(PageNumber + 1))["Next"],
                Span.Id("query-status").Class("text-ui-muted text-sm")[
                    parcels.IsLoading ? "loading…" : parcels.IsPlaceholderData ? "showing the previous page" : "fresh"]
            ],
            Ui.List.Id("query-rows")[(parcels.Data?.Rows ?? []).Select(Row)],
            Div.Class("flex flex-col gap-1")[
                Strong["Picked parcel"],
                P.Id("query-picked").Class("mb-0")[Picked.FetchStatus switch
                {
                    FetchStatus.Paused => "Paused — pick a parcel to load it.",
                    _ when Picked.Data is { } p => $"#{p.Id} to {p.Recipient}: {p.Contents}, {(p.Shipped ? "shipped" : "waiting")}",
                    _ => "loading…",
                }]
            ],
            Div.Class("flex flex-col gap-1")[
                Div.Class("flex gap-2 items-center flex-wrap")[
                    Strong["Parcels holding"],
                    Ui.Button.Id("query-search-books").OnClick(() => _contents = "book")["books"],
                    Ui.Button.Id("query-search-tea").OnClick(() => _contents = "tea")["tea"]
                ],
                P.Id("query-search").Class("mb-0")[Search.Data is { } hits
                    ? $"{_contents}: " + string.Join(", ", hits.Select(p => $"#{p.Id}"))
                    : "loading…"]
            ]
        ];
    }

    private Component Row(Parcel parcel)
    {
        // 4. Keyed by the row, so each row's button has its own pending state.
        var ship = QueryClient.Command<ShipParcel>(key: parcel.Id);

        return Li.Key(parcel.Id).Class("query-row flex gap-2 items-center")[
            Span[$"#{parcel.Id} {parcel.Recipient} — {parcel.Contents}"],
            parcel.Shipped
                ? Span.Class("query-shipped text-ui-ok-ink")["shipped"]
                : Ui.Button.Class("query-ship").Disabled(ship.IsPending)
                    .OnClick(() => ship.SendAsync(new ShipParcel(parcel.Id)))[ship.IsPending ? "shipping…" : "Ship"],
            Ui.Button.Class("query-pick").OnClick(() => _picked = parcel.Id)["Details"]
        ];
    }

    private void GoTo(int page)
    {
        if (page <= 1)
        {
            nav.RemoveQuery("page");
        }
        else
        {
            nav.SetQuery("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
