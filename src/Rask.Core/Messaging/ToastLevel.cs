namespace Rask.Core.Messaging;

/// <summary>
///     Severity of a <see cref="ToastMessage" />. Host-agnostic on purpose — Core carries no Bootstrap
///     dependency, so this is a plain enum rather than a <c>BsColor</c>. A UI layer (e.g.
///     <c>Rask.Bootstrap</c>'s <c>BsToaster</c>) maps each level onto its own colour/icon vocabulary.
/// </summary>
public enum ToastLevel
{
    Info,
    Success,
    Warning,
    Error
}
