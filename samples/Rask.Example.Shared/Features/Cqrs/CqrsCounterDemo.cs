using Rask.Cqrs;

namespace Rask.Example.Shared.Features;

// Injects the umbrella IDispatcher and drives the counter slice: load state with a query on mount,
// send a command on click, then re-read. The awaited handlers re-render automatically — no
// StateHasChanged() by hand.
public sealed partial class CqrsCounterDemo(IDispatcher dispatcher) : Component
{
    private CounterState _view = new(0, []);

    protected override async Task OnMountAsync() =>
        _view = await dispatcher.QueryAsync(new GetCounterState(), CancellationToken);

    private async Task IncrementAsync()
    {
        await dispatcher.SendAsync(new IncrementCounter(1), CancellationToken);
        _view = await dispatcher.QueryAsync(new GetCounterState(), CancellationToken);
    }

    protected override Component? Render() =>
        Div.Id("cqrs-counter").Class("flex flex-col gap-3")[
            Div.Class("flex gap-3 items-center flex-wrap items-center")[
                Span.Id("cqrs-count").Class("text-3xl font-semibold")[$"{_view.Count}"],
                Button.Type("button").Class(Tw.BtnPrimary).Id("cqrs-increment").OnClickAsync(IncrementAsync)["Increment"]
            ],
            _view.Log.Count == 0
                ? P.Class("text-ui-muted text-sm mb-0")["Loading the counter…"]
                : Ul.Id("cqrs-log").Class(Tw.ListGroup)[
                    // The behavior logs every dispatch (the on-mount query included), and the
                    // notification handler adds the "count is now N" line after each command.
                    _view.Log.Select((entry, i) => Li.Key(i).Class($"{Tw.ListGroupItem} text-sm py-1")[entry])
                ]
        ];
}
