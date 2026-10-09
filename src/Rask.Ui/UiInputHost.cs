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

    /// <summary>
    ///     The keys <see cref="OnKeyDown" /> acts on, by <c>KeyboardEvent.key</c> with a space between. The
    ///     runtime sends no other: a letter typed is the input's <c>input</c>, not a second round trip.
    /// </summary>
    internal required string Keys { get; init; }

    /// <summary>
    ///     The keys that empty the input, in the browser and at the key: emptied from the handler a round trip
    ///     later, it would lose what was typed since.
    /// </summary>
    internal string? ClearKeys { get; init; }

    /// <summary>What a click in the input does to the list.</summary>
    internal required Action OnClick { get; init; }

    /// <summary>The input's marks with what the runtime is asked for over them: which keys to send, which empty it.</summary>
    /// <param name="input">What marks the input as Flux's.</param>
    internal Dictionary<string, string?> MarksOver(IReadOnlyDictionary<string, string?> input)
    {
        var marks = new Dictionary<string, string?>(input, StringComparer.Ordinal) { ["data-rask-keys"] = Keys };
        if (ClearKeys is not null)
        {
            marks["data-rask-clear-keys"] = ClearKeys;
        }

        return marks;
    }

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
