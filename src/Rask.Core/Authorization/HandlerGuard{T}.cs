using Microsoft.AspNetCore.Authorization;

namespace Rask.Core.Authorization;

/// <inheritdoc cref="HandlerGuard" />
/// <typeparam name="T">The argument the event carries.</typeparam>
internal sealed class HandlerGuard<T>(Delegate inner, IAuthorizeData[] rule) : IHandlerAdapter
{
    public Delegate Inner => inner;

    public static Action<T> Guard(Action<T> handler) =>
        HandlerRules.Of(handler) is { } rule ? new HandlerGuard<T>(handler, rule).Run : handler;

    public static Func<T, Task> Guard(Func<T, Task> handler) =>
        HandlerRules.Of(handler) is { } rule ? new HandlerGuard<T>(handler, rule).RunAsync : handler;

    public static Func<T, ValueTask> Guard(Func<T, ValueTask> handler) =>
        HandlerRules.Of(handler) is { } rule ? new HandlerGuard<T>(handler, rule).RunValueAsync : handler;

    private void Run(T arg)
    {
        HandlerAccess.DemandNow(rule, HandlerRules.Name(inner));
        ((Action<T>)inner)(arg);
    }

    private async Task RunAsync(T arg)
    {
        await HandlerAccess.Demand(rule, HandlerRules.Name(inner));
        await ((Func<T, Task>)inner)(arg);
    }

    private async ValueTask RunValueAsync(T arg)
    {
        await HandlerAccess.Demand(rule, HandlerRules.Name(inner));
        await ((Func<T, ValueTask>)inner)(arg);
    }
}
