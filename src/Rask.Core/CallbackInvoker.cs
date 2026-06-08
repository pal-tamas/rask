namespace Rask.Core;

/// <summary>
///     Shared invocation core for <see cref="Callback" /> / <see cref="Callback{T}" /> and the
///     static <c>Callback.InvokeAsync(...)</c> helpers. Pattern-matches the common delegate
///     shapes (no <c>DynamicInvoke</c> on the hot path), awaits async ones, and then re-renders
///     the captured receiver via <see cref="Component.StateHasChanged" />.
/// </summary>
internal static class CallbackInvoker
{
    public static async ValueTask InvokeAsync(Component? receiver, Delegate? @delegate)
    {
        switch (@delegate)
        {
            case null:
                return;
            case Action a:
                a();
                break;
            case Func<Task> f:
                await f().ConfigureAwait(false);
                break;
            default:
                @delegate.DynamicInvoke();
                break;
        }

        // Re-render the component that owns the callback. When this runs inside an event
        // dispatch the post-handler render picks the flag up; when it runs off the dispatch
        // path (timer, lifecycle continuation) StateHasChanged schedules the render itself.
        receiver?.StateHasChanged();
    }

    public static async ValueTask InvokeAsync<T>(Component? receiver, Delegate? @delegate, T arg)
    {
        switch (@delegate)
        {
            case null:
                return;
            case Action<T> a:
                a(arg);
                break;
            case Func<T, Task> f:
                await f(arg).ConfigureAwait(false);
                break;
            // Tolerate an arg-less handler wired to a typed callback (the parent ignored the arg).
            case Action a0:
                a0();
                break;
            case Func<Task> f0:
                await f0().ConfigureAwait(false);
                break;
            default:
                @delegate.DynamicInvoke(arg);
                break;
        }

        receiver?.StateHasChanged();
    }
}
