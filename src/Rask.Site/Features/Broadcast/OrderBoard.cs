using Rask.Core.Messaging;

namespace Rask.Site.Features;

// Subscribes once, on mount. There is nothing to unsubscribe and no StateHasChanged: the subscription ends when the
// board unmounts, and the board re-renders after each message the way it would after a click.
public sealed partial class OrderBoard(IBroadcast broadcast) : Component
{
    private readonly List<string> _orders = [];

    public required string Title { get; set; }

    protected override async Task OnMount() =>
        broadcast.Subscribe(this, BroadcastDemo.Orders, order => _orders.Insert(0, order));

    protected override Component? Render() =>
        UiCard.Class("broadcast-board")[
            H3.Class("text-sm font-semibold")[Title],
            _orders.Count == 0
                ? P.Class("text-sm text-ui-muted")["No orders yet."]
                : Ul.Class("text-sm")[_orders.Select(order => Li.Key(order)[order])]
        ];
}
