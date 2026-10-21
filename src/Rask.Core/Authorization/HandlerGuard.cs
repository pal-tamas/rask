using Microsoft.AspNetCore.Authorization;

namespace Rask.Core.Authorization;

/// <summary>
///     A method group whose method carries <c>[Authorize]</c>, held behind the check: <c>.OnClick(Remove)</c> runs
///     <c>Remove</c> only for a person who meets it. Any other handler is handed back as it was.
/// </summary>
/// <remarks>
///     Applied once, where the handler enters a <see cref="Callback" /> or an <see cref="AutoCallback" /> wrap, so
///     every path that later runs it — the DOM dispatch, <see cref="Callback.Invoke" />, a form's submit — runs the
///     check. The shape is kept: a synchronous handler stays synchronous and is checked without waiting.
/// </remarks>
internal sealed class HandlerGuard(Delegate inner, IAuthorizeData[] rule) : IHandlerAdapter
{
    public Delegate Inner => inner;

    public static Action Guard(Action handler) =>
        HandlerRules.Of(handler) is { } rule ? new HandlerGuard(handler, rule).Run : handler;

    public static Func<Task> Guard(Func<Task> handler) =>
        HandlerRules.Of(handler) is { } rule ? new HandlerGuard(handler, rule).RunAsync : handler;

    public static Func<ValueTask> Guard(Func<ValueTask> handler) =>
        HandlerRules.Of(handler) is { } rule ? new HandlerGuard(handler, rule).RunValueAsync : handler;

    private void Run()
    {
        HandlerAccess.DemandNow(rule, HandlerRules.Name(inner));
        ((Action)inner)();
    }

    private async Task RunAsync()
    {
        await HandlerAccess.Demand(rule, HandlerRules.Name(inner));
        await ((Func<Task>)inner)();
    }

    private async ValueTask RunValueAsync()
    {
        await HandlerAccess.Demand(rule, HandlerRules.Name(inner));
        await ((Func<ValueTask>)inner)();
    }
}
