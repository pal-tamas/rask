using System.ComponentModel;
using Rask.Core.Forms;

namespace Rask.Values;

/// <summary>
///     What a form over a model of its own holds of a value object of several values while its members are typed in one
///     at a time: the generated <c>Opening.__Draft</c>, a property per member of the record, each holding what was typed.
///     Every member typed in makes the value object again through its <c>TryCreate</c>, and the model's property is set
///     only once its rules take the whole of it. Made by generated code; not an API to write against.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ValueDraft
{
    /// <summary>What a field bound to a member of this draft writes through: the model's property that holds the value object.</summary>
    internal DraftBinding? Binding { get; set; }

    /// <summary>Whether every member is empty: text blank, anything else null.</summary>
    protected abstract bool HoldsNothing { get; }

    /// <summary>Called by a member's setter: a member was typed into, so the value object is made again.</summary>
    protected void OnTyped() => Binding?.Typed();

    /// <summary>Fills every member from the value object, or empties every one for none, without counting as typing.</summary>
    /// <param name="value">The value object, or null.</param>
    protected abstract void FillFrom(object? value);

    /// <summary>The value object made from what the members hold: a member that may not be empty and is, said under it, then its own rules.</summary>
    /// <param name="problems">What was said against it; empty when it was made.</param>
    /// <returns>The value object, or null when it was refused.</returns>
    protected abstract object? TryMake(out IReadOnlyList<Problem> problems);

    internal bool IsEmpty => HoldsNothing;

    internal void Fill(object? value) => FillFrom(value);

    internal object? Make(out IReadOnlyList<Problem> problems) => TryMake(out problems);
}
