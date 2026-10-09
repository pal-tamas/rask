using System.ComponentModel;
using Rask.Core.Forms;

namespace Rask.Core;

public static partial class BuilderRuntime
{
    /// <summary>
    ///     What a <c>Validate</c> step leaves on a control that already holds a rule: the rule written
    ///     earlier, then this one if the earlier one let the value through.
    /// </summary>
    /// <remarks>
    ///     The one step that adds to what an earlier step wrote instead of replacing it. Safe because a rule
    ///     is put back to nothing when the chain opens, so each render composes from empty. A
    ///     <see langword="null" /> adds nothing, which is what forwarding an optional rule needs.
    /// </remarks>
    /// <param name="earlier">The rule the chain has written so far.</param>
    /// <param name="later">The rule this step adds.</param>
    /// <typeparam name="T">The value being validated.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Validator<T>? Then<T>(Validator<T>? earlier, Validator<T>? later)
    {
        if (later?.Rule is not { } second)
        {
            return earlier;
        }

        return earlier?.Rule is { } first ? new Validator<T>(RuleSequence<T>.Of(first, second)) : later;
    }
}
