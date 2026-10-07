using System.Linq.Expressions;

namespace Rask;

/// <summary>
///     What a control built around <see cref="UiInput{T}" /> adds to it: the list <c>Ui.Autocomplete</c> drops
///     under the input, and the member that control is bound to.
/// </summary>
/// <remarks>
///     Internal, so it is no step of <c>Ui.Input</c>'s chain: the input stays Flux's input, and the control
///     around it says here what its own list needs of the text box.
/// </remarks>
internal sealed class UiInputHost
{
    /// <summary>The member the control around the input is bound to: whose message the field shows.</summary>
    internal LambdaExpression? Bound { get; init; }

    /// <summary>The name the list is anchored to, without its dashes. Stated on the input's box.</summary>
    internal required string AnchorName { get; init; }

    /// <summary>The combobox's ARIA: which list, whether it is open, which row the cursor is on.</summary>
    internal required IReadOnlyDictionary<string, string?> Aria { get; init; }

    /// <summary>What a key pressed in the input does to the list.</summary>
    internal required Func<KeyboardEvent, Task> OnKeyDown { get; init; }

    /// <summary>What a click in the input does to the list.</summary>
    internal required Action OnClick { get; init; }

    /// <summary>The field's ARIA with the combobox's over it.</summary>
    /// <param name="field">What the field says of the input: invalid, described by.</param>
    internal Dictionary<string, string?> AriaOver(IReadOnlyDictionary<string, string?> field)
    {
        var aria = new Dictionary<string, string?>(field, StringComparer.Ordinal);
        foreach (var (name, value) in Aria)
        {
            aria[name] = value;
        }

        return aria;
    }
}
