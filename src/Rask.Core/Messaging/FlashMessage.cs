namespace Rask.Core.Messaging;

/// <summary>
///     One transient user-facing message queued on <see cref="IFlash" /> and drained by a
///     <c>FlashOutlet</c>. <see cref="Id" /> is a per-session monotonic identity assigned by the
///     service so a UI layer can key its rendered elements (and dismiss one by id) without inventing
///     its own.
/// </summary>
public sealed record FlashMessage(int Id, FlashLevel Level, string Message, string? Title = null);
