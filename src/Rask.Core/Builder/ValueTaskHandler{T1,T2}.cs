namespace Rask.Core;

/// <inheritdoc cref="ValueTaskHandler" />
/// <typeparam name="T1">The first argument the event carries.</typeparam>
/// <typeparam name="T2">The second argument the event carries.</typeparam>
internal sealed class ValueTaskHandler<T1, T2>(Func<T1, T2, ValueTask> inner) : IHandlerAdapter
{
    public Delegate Inner => inner;

    public static Func<T1, T2, Task> Adapt(Func<T1, T2, ValueTask> handler) =>
        new ValueTaskHandler<T1, T2>(handler).Invoke;

    private Task Invoke(T1 arg1, T2 arg2) => inner(arg1, arg2).AsTask();
}
