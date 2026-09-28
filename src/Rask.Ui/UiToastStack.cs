namespace Rask;

/// <summary>Marks the subtree inside a <see cref="UiToaster" />, so a toast knows it is one of a stack.</summary>
/// <remarks>
/// A toast on its own places itself in a corner of the viewport. Inside a toaster the STACK is placed and each
/// toast fills its width, because two toasts that each pinned themselves to the same corner would sit on top
/// of one another.
/// </remarks>
internal sealed record UiToastStack
{
    /// <summary>The one marker every toaster provides; it carries nothing, so one is enough.</summary>
    internal static readonly UiToastStack Instance = new();
}
