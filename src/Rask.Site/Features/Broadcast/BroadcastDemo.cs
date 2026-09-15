using Rask.Core.Messaging;

namespace Rask.Site.Features;

// Publishes on a topic. It holds no reference to the boards below — they only share the topic — so a board added
// anywhere else on the page (or, on a server, in anyone else's open tab) would receive the same message.
public sealed partial class BroadcastDemo(IBroadcast broadcast) : Component
{
    internal static readonly Topic<string> Orders = new("docs.orders");

    private static readonly string[] Items = ["anvil", "rocket skates", "giant magnet", "earthquake pills"];

    private int _placed;

    protected override Component? Render() =>
        Div.Id("broadcast-demo").Class("flex flex-col gap-3")[
            UiButton.Tone(UiTone.Primary).Id("broadcast-publish").OnClick(PlaceOrderAsync)["Place an order"],
            Div.Class("grid gap-3 sm:grid-cols-2")[
                OrderBoard.Title("Warehouse"),
                OrderBoard.Title("Accounting")
            ]
        ];

    private async Task PlaceOrderAsync() =>
        await broadcast.PublishAsync(Orders, $"#{++_placed} {Items[(_placed - 1) % Items.Length]}");
}
