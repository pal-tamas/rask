namespace Rask.Core.Messaging;

/// <summary>
///     One transient user-facing message queued on <see cref="IToaster" /> and drained by a
///     <c>ToastOutlet</c>. <see cref="Id" /> is a per-session monotonic identity assigned by the
///     service so a UI layer can key its rendered elements (and dismiss one by id) without inventing
///     its own.
/// </summary>
public sealed record ToastMessage(int Id, ToastLevel Level, string Message, string? Title = null)
{
    /// <summary>
    ///     How long it shows: <c>null</c> for the app's default, <see cref="Timeout.InfiniteTimeSpan" /> until the
    ///     person dismisses it.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>A button on the toast — <c>Toast.Info("Deleted").Action("Undo", …)</c> — or <c>null</c>.</summary>
    public ToastAction? Action { get; init; }
}
