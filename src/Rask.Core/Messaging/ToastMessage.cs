namespace Rask.Core.Messaging;

/// <summary>
///     One transient user-facing message queued on <see cref="IToaster" /> and drained by a
///     <c>ToastOutlet</c>. <see cref="Id" /> is a per-session monotonic identity assigned by the
///     service so a UI layer can key its rendered elements (and dismiss one by id) without inventing
///     its own.
/// </summary>
public sealed record ToastMessage(int Id, ToastLevel Level, string Message, string? Title = null);
