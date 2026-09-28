namespace Rask.Core.Forms;

/// <summary>What <see cref="EditContext.FieldChanged" /> reports: the field whose value changed.</summary>
/// <param name="field">The field that changed.</param>
public sealed class FieldChangedEventArgs(FieldIdentifier field) : EventArgs
{
    /// <summary>The field that changed.</summary>
    public FieldIdentifier Field { get; } = field;
}
