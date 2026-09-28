using Rask.Cqrs;

namespace Rask.Site.Features;

public sealed class IncrementCounterHandler(CqrsCounterStore store, IDispatcher dispatcher)
    : ICommandHandler<IncrementCounter, int>
{
    public async Task<int> Handle(IncrementCounter command)
    {
        var value = store.IncrementBy(command.By);
        await dispatcher.Publish(new CounterIncremented(value), Current.Cancellation);
        return value;
    }
}
