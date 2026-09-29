namespace Rask.Core.Messaging;

/// <summary>
///     A toast just raised — <c>Toast.Success("Saved")</c> — and the steps that finish it before it is drawn:
///     <c>.Title("Order 42")</c>, <c>.Action("Undo", …)</c>, <c>.For(30.Seconds)</c>, <c>.UntilDismissed()</c>.
/// </summary>
public readonly struct ShowingToast
{
    private readonly IToastQueue? _queue;
    private readonly int _id;

    internal ShowingToast(IToastQueue queue, int id)
    {
        _queue = queue;
        _id = id;
    }

    /// <summary>A heading above the message.</summary>
    /// <param name="title">The heading.</param>
    public ShowingToast Title(string title) => Change(m => m with { Title = title });

    /// <summary>A button on the toast; pressing it runs <paramref name="run" /> and dismisses the toast.</summary>
    /// <param name="label">The button's text — <c>"Undo"</c>.</param>
    /// <param name="run">What pressing it does.</param>
    public ShowingToast Action(string label, Action run) => WithAction(label, new Callback(run));

    /// <summary>A button on the toast; pressing it awaits <paramref name="run" /> and dismisses the toast.</summary>
    /// <param name="label">The button's text — <c>"Undo"</c>.</param>
    /// <param name="run">What pressing it does — <c>() =&gt; Order.Delete(id)</c>.</param>
    public ShowingToast Action(string label, Func<Task> run) => WithAction(label, new Callback(run));

    private ShowingToast WithAction(string label, Callback run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Change(m => m with { Action = new ToastAction(label, run) });
    }

    /// <summary>How long this one shows, instead of the app's default: <c>.For(30.Seconds)</c>.</summary>
    /// <param name="duration">How long; zero or less is the app's default.</param>
    public ShowingToast For(TimeSpan duration) => Change(m => m with { Duration = duration > TimeSpan.Zero ? duration : null });

    /// <summary>Shows until the person dismisses it.</summary>
    public ShowingToast UntilDismissed() => Change(static m => m with { Duration = Timeout.InfiniteTimeSpan });

    private ShowingToast Change(Func<ToastMessage, ToastMessage> change)
    {
        _queue?.Change(_id, change);
        return this;
    }
}
