using Rask.Cqrs;

namespace Rask.Site.Features;

public sealed class GetCounterStateHandler(CqrsCounterStore store) : IQueryHandler<GetCounterState, CounterState>
{
    public Task<CounterState> Handle(GetCounterState query) =>
        Task.FromResult(new CounterState(store.Count, store.Log));
}
