using Rask.Cqrs;
using Rask.Query;

namespace Rask.Site.Features;

// Publishes through the dispatcher, the same call a command handler or a background job makes. It holds no reference to
// the boards below — they only subscribe to OrderPlaced — so a board anywhere else (or, on a server, in anyone else's
// open tab) would receive the same order.
public sealed partial class SubscriptionDemo(IDispatcher dispatcher) : Component
{
    private static readonly string[] Items = ["anvil", "rocket skates", "giant magnet", "earthquake pills"];

    private int _placed;

    // Scoped to one order, and following it: the lambda runs at every read, and returns null — waiting — until an order
    // has been placed.
    private Subscription<OrderShipped> Shipped => field ??= QueryClient.Subscribe<OrderShipped>(() => _placed > 0 ? _placed : null);

    protected override Component? Render() =>
        Div.Id("subscription-demo").Class("flex flex-col gap-3")[
            Div.Class("flex gap-2 flex-wrap")[
                Ui.Button.Tone(Ui.Tone.Primary).Id("subscription-place").OnClick(PlaceOrderAsync)["Place an order"],
                Ui.Button.Id("subscription-ship").Disabled(_placed == 0).OnClick(ShipOrderAsync)["Ship it"]
            ],
            P.Id("subscription-tracked").Class("mb-0")[Tracking()],
            Div.Class("grid gap-3 sm:grid-cols-2")[
                OrderBoard.Title("Warehouse"),
                OrderBoard.Title("Accounting")
            ]
        ];

    private string Tracking() => _placed switch
    {
        0 => "Place an order to track it.",
        _ when Shipped.Data?.Number == _placed => $"Order #{_placed} has shipped.",
        _ => $"Order #{_placed} is waiting to ship.",
    };

    private Task PlaceOrderAsync() =>
        dispatcher.PublishAsync(new OrderPlaced(++_placed, Items[(_placed - 1) % Items.Length]));

    private Task ShipOrderAsync() => dispatcher.PublishAsync(new OrderShipped(_placed));
}
