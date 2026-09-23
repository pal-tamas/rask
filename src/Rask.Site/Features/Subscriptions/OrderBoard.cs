using Rask.Query;

namespace Rask.Site.Features;

// Subscribes in Render, like a query. Nothing to unsubscribe and no StateHasChanged: each order re-renders the board, and
// the subscription closes when the board unmounts. Keep(5) holds the last five, oldest first.
public sealed partial class OrderBoard : Component
{
    public required string Title { get; set; }

    protected override Component? Render()
    {
        var orders = QueryClient.Subscribe<OrderPlaced>().Keep(5);

        return UiCard.Class("subscription-board")[
            H3.Class("text-sm font-semibold")[Title],
            orders.Items.Count == 0
                ? P.Class("text-sm text-ui-muted")["No orders yet."]
                : Ul.Class("text-sm")[orders.Items.Reverse().Select(order => Li.Key(order.Number)[$"#{order.Number} {order.Item}"])]
        ];
    }
}
