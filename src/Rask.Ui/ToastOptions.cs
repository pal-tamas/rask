namespace Rask;

/// <summary>
///     Where the app's toasts appear and how long they stay, unless a toast says otherwise:
///     <c>c.Toasts.At(Ui.Position.Top, Ui.Align.End).For(8.Seconds)</c>, or <c>Rask:Toasts</c> in appsettings.
/// </summary>
public sealed class ToastOptions
{
    /// <summary>The edge they stack against. Bottom when unset.</summary>
    public Ui.Position Position { get; set; } = Ui.Position.Bottom;

    /// <summary>Where along that edge. The end when unset.</summary>
    public Ui.Align Align { get; set; } = Ui.Align.End;

    /// <summary>How long a toast shows before it goes by itself. Five seconds when unset.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Stacks toasts against <paramref name="position" />, at <paramref name="align" /> along it.</summary>
    /// <param name="position">The edge — top or bottom.</param>
    /// <param name="align">Where along it — start, center or end.</param>
    public ToastOptions At(Ui.Position position, Ui.Align align)
    {
        Position = position;
        Align = align;
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
