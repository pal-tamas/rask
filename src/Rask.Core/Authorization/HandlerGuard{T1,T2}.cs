using Microsoft.AspNetCore.Authorization;

namespace Rask.Core.Authorization;

/// <inheritdoc cref="HandlerGuard" />
/// <typeparam name="T1">The first argument the event carries.</typeparam>
/// <typeparam name="T2">The second argument the event carries.</typeparam>
internal sealed class HandlerGuard<T1, T2>(Delegate inner, IAuthorizeData[] rule) : IHandlerAdapter
{
    public Delegate Inner => inner;

    public static Action<T1, T2> Guard(Action<T1, T2> handler) =>
        HandlerRules.Of(handler) is { } rule ? new HandlerGuard<T1, T2>(handler, rule).Run : handler;

    public static Func<T1, T2, Task> Guard(Func<T1, T2, Task> handler) =>
        HandlerRules.Of(handler) is { } rule ? new HandlerGuard<T1, T2>(handler, rule).RunAsync : handler;

    public static Func<T1, T2, ValueTask> Guard(Func<T1, T2, ValueTask> handler) =>
        HandlerRules.Of(handler) is { } rule ? new HandlerGuard<T1, T2>(handler, rule).RunValueAsync : handler;

    private void Run(T1 arg1, T2 arg2)
    {
        HandlerAccess.DemandNow(rule, HandlerRules.Name(inner));
        ((Action<T1, T2>)inner)(arg1, arg2);
    }

    private async Task RunAsync(T1 arg1, T2 arg2)
    {
        await HandlerAccess.Demand(rule, HandlerRules.Name(inner));
        await ((Func<T1, T2, Task>)inner)(arg1, arg2);
    }

    private async ValueTask RunValueAsync(T1 arg1, T2 arg2)
    {
        await HandlerAccess.Demand(rule, HandlerRules.Name(inner));
        await ((Func<T1, T2, ValueTask>)inner)(arg1, arg2);
    }
}
