namespace Rask;

/// <summary>
///     Where the app's toasts appear and how long they stay, unless the app places a <c>Ui.Toast</c> of its
///     own: <c>c.Toasts.At(Ui.ToastPosition.TopEnd).For(8.Seconds)</c>, or <c>Rask:Toasts</c> in appsettings.
/// </summary>
public sealed class ToastOptions
{
    /// <summary>The corner they appear in. The bottom end when unset.</summary>
    public Ui.ToastPosition Position { get; set; } = Ui.ToastPosition.BottomEnd;

    /// <summary>How long a toast shows before it goes by itself. Five seconds when unset.</summary>
    public TimeSpan Duration { get; set; } = UiToast.DefaultDuration;

    /// <summary>Shows toasts in <paramref name="position" />: <c>.At(Ui.ToastPosition.TopEnd)</c>.</summary>
    /// <param name="position">The corner.</param>
    public ToastOptions At(Ui.ToastPosition position)
    {
        Position = position;
        return this;
    }

    /// <summary>How long a toast shows by default: <c>.For(8.Seconds)</c>.</summary>
    /// <param name="duration">How long.</param>
    public ToastOptions For(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        Duration = duration;
        return this;
    }
}
