namespace Rask.Core;

/// <summary>
///     <c>Invoke</c> on an event property itself — declared <c>Callback?</c> — so firing or forwarding an event needs no
///     <c>?.</c> and no <c>??</c>: <c>await OnPick.Invoke();</c>, <c>.OnClick(() =&gt; OnRate.Invoke(i))</c>. An unset
///     property returns the cached <see cref="Task.CompletedTask" />, exactly as an unset slot does.
/// </summary>
public static class CallbackExtensions
{
    /// <inheritdoc cref="Callback.Invoke" />
    /// <param name="callback">The event property; unset is a no-op.</param>
    public static Task Invoke(this Callback? callback) =>
        callback is { } set ? set.Invoke() : Task.CompletedTask;

    /// <inheritdoc cref="Callback{T}.Invoke" />
    /// <param name="callback">The event property; unset is a no-op.</param>
    /// <param name="arg">The argument the event carries.</param>
    public static Task Invoke<T>(this Callback<T>? callback, T arg) =>
        callback is { } set ? set.Invoke(arg) : Task.CompletedTask;

    /// <inheritdoc cref="Callback{T1, T2}.Invoke" />
    /// <param name="callback">The event property; unset is a no-op.</param>
    /// <param name="arg1">The first argument the event carries.</param>
    /// <param name="arg2">The second argument the event carries.</param>
    public static Task Invoke<T1, T2>(this Callback<T1, T2>? callback, T1 arg1, T2 arg2) =>
        callback is { } set ? set.Invoke(arg1, arg2) : Task.CompletedTask;
}
