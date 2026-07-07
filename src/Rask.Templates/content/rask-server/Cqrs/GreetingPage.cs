using Rask.Cqrs;
using Rask.Core.Routing;

namespace Company.RaskServer;

// Injects the umbrella IDispatcher and dispatches GreetingQuery — on mount, and again on each button
// click. The awaited dispatch re-renders this component automatically, so there's no StateHasChanged()
// by hand. This is the whole CQRS round-trip: a page sends a message, a handler (in
// Cqrs/GreetingQuery.cs) answers it, decoupled from the UI. See docs/cqrs.md.
[Route("/greeting")]
public sealed class GreetingPage(IDispatcher dispatcher) : Component
{
    private static readonly string[] Names = ["world", "Ada", "Grace", "Linus"];
    private int _index;
    private string _greeting = "";

    protected override async Task OnMountAsync() =>
        _greeting = await dispatcher.DispatchAsync(new GreetingQuery(Names[_index]), CancellationToken);

    private async Task GreetNextAsync()
    {
        _index = (_index + 1) % Names.Length;
        _greeting = await dispatcher.DispatchAsync(new GreetingQuery(Names[_index]), CancellationToken);
    }

    protected override Component? Render() =>
        [
            H1()["CQRS greeting"],
            P()["Each click dispatches a GreetingQuery through the mediator; a handler answers it."],
            P(Id: "greeting", Class: "fs-4 fw-semibold")[_greeting],
            BsButton(Color: BsColor.Primary, OnClickAsync: GreetNextAsync)["Greet the next name"]
        ];
}
