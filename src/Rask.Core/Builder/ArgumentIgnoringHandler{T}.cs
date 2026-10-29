namespace Rask.Core;

/// <summary>
///     A handler written without the argument its slot carries — <c>.OnFiles(() =&gt; n++)</c> — registered in the
///     slot's own shape. Bare, it is a parameterless delegate, which the dispatch reads as a DOM-event handler and
///     refuses the slot's frame for as a stale id: the handler silently never ran.
/// </summary>
/// <typeparam name="T">The argument the slot's frames carry.</typeparam>
internal sealed class ArgumentIgnoringHandler<T>(Delegate inner) : IHandlerAdapter
{
    public Delegate Inner => inner;

    /// <summary><paramref name="handler" /> in the slot's shape; one that already takes the argument is returned as is.</summary>
    public static Delegate Fed(Delegate handler) => handler switch
    {
        Action => new Action<T>(new ArgumentIgnoringHandler<T>(handler).Run),
        Func<Task> => new Func<T, Task>(new ArgumentIgnoringHandler<T>(handler).RunAsync),
        _ => handler,
    };

    private void Run(T _) => ((Action)inner)();

    private Task RunAsync(T _) => ((Func<Task>)inner)();
}
