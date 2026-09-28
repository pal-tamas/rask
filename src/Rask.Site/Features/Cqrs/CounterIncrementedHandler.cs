using Rask.Cqrs;

namespace Rask.Site.Features;

public sealed class CounterIncrementedHandler(CqrsCounterStore store) : INotificationHandler<CounterIncremented>
{
    public Task Handle(CounterIncremented notification)
    {
        store.Note($"🔔 count is now {notification.Value}");
        return Task.CompletedTask;
    }
}
