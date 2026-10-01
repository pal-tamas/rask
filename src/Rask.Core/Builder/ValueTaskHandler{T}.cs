namespace Rask.Core;

/// <inheritdoc cref="ValueTaskHandler" />
/// <typeparam name="T">The argument the event carries.</typeparam>
internal sealed class ValueTaskHandler<T>(Func<T, ValueTask> inner) : IHandlerAdapter
{
    public Delegate Inner => inner;

    public static Func<T, Task> Adapt(Func<T, ValueTask> handler) => new ValueTaskHandler<T>(handler).Invoke;

    private Task Invoke(T arg) => inner(arg).AsTask();
}
