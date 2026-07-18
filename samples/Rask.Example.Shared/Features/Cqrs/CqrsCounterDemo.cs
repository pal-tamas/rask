using Rask.Bootstrap;
using Rask.Cqrs;

namespace Rask.Example.Shared.Features;

// Injects the umbrella IDispatcher and drives the counter slice: load state with a query on mount,
// send a command on click, then re-read. The awaited handlers re-render automatically — no
// StateHasChanged() by hand.
public sealed class CqrsCounterDemo(IDispatcher dispatcher) : Component
{
    private CounterState _view = new(0, []);

    protected override async Task OnMountAsync() =>
        _view = await dispatcher.DispatchAsync(new GetCounterState(), CancellationToken);

    private async Task IncrementAsync()
    {
        await dispatcher.DispatchAsync(new IncrementCounter(1), CancellationToken);
        _view = await dispatcher.DispatchAsync(new GetCounterState(), CancellationToken);
    }

    protected override Component? Render() =>
        Div(Id: "cqrs-counter", Class: "vstack gap-3")[
            BsStack(Gap: 3, Align: BsAlign.Center)[
                Span(Id: "cqrs-count", Class: "display-6 fw-semibold")[$"{_view.Count}"],
                BsButton(Color: BsColor.Primary, Id: "cqrs-increment", OnClickAsync: IncrementAsync)["Increment"]
            ],
            _view.Log.Count == 0
                ? P(Class: "text-secondary small mb-0")["Loading the counter…"]
                : Ul(Id: "cqrs-log", Class: "list-group")[
                    // The behavior logs every dispatch (the on-mount query included), and the
                    // notification handler adds the "count is now N" line after each command.
                    _view.Log.Select((entry, i) => Li(Key: i, Class: "list-group-item small py-1")[entry])
                ]
        ];
}
