namespace Rask.Core;

/// <summary>
///     A handler that returns a <see cref="ValueTask" /> — <c>() =&gt; OnRate.Invoke(i)</c> — held as the
///     <see cref="Task" />-returning shape the runtime dispatches.
/// </summary>
/// <remarks>
///     Adapted once, where the handler enters a <see cref="Callback" />, rather than recognised everywhere a handler
///     is read: the DOM dispatch, the frame-shape check and the invokers each switch on the delegate's shape, and a
///     shape one of them did not know was a click that silently did nothing. <c>AsTask()</c> on a ValueTask that
///     completed synchronously hands back the cached completed task, so the forwarding costs this one object.
/// </remarks>
internal sealed class ValueTaskHandler(Func<ValueTask> inner) : IHandlerAdapter
{
    public Delegate Inner => inner;

    public static Func<Task> Adapt(Func<ValueTask> handler) => new ValueTaskHandler(handler).Invoke;

    private Task Invoke() => inner().AsTask();
}
