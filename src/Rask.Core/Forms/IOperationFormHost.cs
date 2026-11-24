using System.ComponentModel;

namespace Rask.Core.Forms;

/// <summary>
///     What holds an operation's form — <c>Ui.Editor</c> — told by the form, which finds it in its context: what to draw
///     for an id no row has, and that a submit was saved. Not an API to write against.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IOperationFormHost
{
    /// <summary>What a form inside draws for an id no row has, when it was given no <c>Missing</c> of its own.</summary>
    Component? Missing { get; }

    /// <summary>Told once a form inside has saved: a toast, the dialog closed.</summary>
    /// <param name="message">What the form said to toast (<c>.Saved("…")</c>); null for the host's own word.</param>
    /// <returns>A task that completes once the host has done so.</returns>
    Task Saved(string? message);
}
