namespace Rask.Core.Messaging;

/// <summary>
///     Severity of a <see cref="ToastMessage" />. Host-agnostic on purpose — Core carries no UI kit
///     dependency, so this is a plain enum rather than a <c>Ui.Tone</c>. A UI layer (e.g.
///     <c>Rask.Ui</c>'s <c>Ui.Toaster</c>) maps each level onto its own colour/icon vocabulary.
/// </summary>
public enum ToastLevel
{
    Info,
    Success,
    Warning,
    Error
}
