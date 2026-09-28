using Rask.Cqrs;

namespace Rask.Site.Features;

// --- Pipeline behavior (decorator): the extension point for cross-cutting concerns. This one logs
//     every dispatch; a real app would add logging/validation/transactions the same way. Register it
//     with `AddRaskCqrs(o => o.AddOpenBehavior(typeof(DispatchLogBehavior<,>)))`. ---
public sealed class DispatchLogBehavior<TRequest, TResult>(CqrsCounterStore store)
    : IPipelineBehavior<TRequest, TResult>
{
    public Task<TResult> Handle(TRequest request, RequestHandler<TResult> next)
    {
        store.Note($"⚙ dispatch {typeof(TRequest).Name}");
        return next();
    }
}
