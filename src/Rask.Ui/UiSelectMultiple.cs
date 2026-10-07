using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux's <c>flux:select</c> with <c>multiple</c>: several answers out of a list of options, held in a
///     collection.
/// </summary>
/// <typeparam name="T">The type of one answer: what each option's <c>Value</c> is.</typeparam>
/// <remarks>
///     <para>
///     <see cref="UiSelect{T}" />'s twin, and the same <c>Ui.Select</c> at the call site: the chain builds this
///     one when it is opened on a collection — <c>Ui.Select.Bind(() =&gt; model.Tags)</c> over a
///     <c>List&lt;T&gt;</c>, a <c>T[]</c> or a <c>HashSet&lt;T&gt;</c>, or <c>Ui.Select.Values(tags)</c> with
///     <see cref="OnChange" />. A form control is keyed on one type, and one answer and a collection of them
///     are two.
///     </para>
///     <code>
///     Ui.Select.Bind(() =&gt; model.Tags).Listbox.Multiple().Placeholder("Choose industries...")[
///         industries.Select(name =&gt; Ui.SelectOption[name])
///     ]
///     </code>
///     <para>
///     Several answers are the listbox's, as on Flux: the native variant holds one. Picking a row switches it
///     and leaves the list open; the button names the one picked option, or counts them.
///     </para>
/// </remarks>
[RaskChainEntry("UiSelect")]
public sealed partial class UiSelectMultiple<T> : UiSelectControl<T>, IFormControl<ICollection<T>>
{
    /// <summary>The answers, when the page holds them. Pair it with <see cref="OnChange" />.</summary>
    public ICollection<T>? Value { get; set; }

    /// <summary>Called with a new collection of answers when the page holds them.</summary>
    public Callback<ICollection<T>> OnChange { get; set; }

    /// <summary>The collection the select reads and writes: Flux's <c>wire:model</c>.</summary>
    public Expression<Func<ICollection<T>>>? Bind { get; set; }

    /// <summary>A rule for this field, run with the form's own.</summary>
    public Validator<ICollection<T>>? Validate { get; set; }

    /// <summary>Called after a pick has been written to the bound collection and validated.</summary>
    public Callback<ICollection<T>> AfterBind { get; set; }

    /// <inheritdoc />
    private protected override LambdaExpression? Bound => Bind;

    /// <inheritdoc />
    private protected override bool HoldsMany => true;

    /// <inheritdoc />
    private protected override IReadOnlyList<T> Current() =>
        UiFormCommit.Resolve(this).Current is { } current ? [.. current] : [];

    /// <inheritdoc />
    private protected override Task CommitAsync(IReadOnlyList<T> picked)
    {
        var (accessor, context, _) = UiFormCommit.Resolve(this);

        return UiFormCommit.CommitSelectionAsync(this, accessor, context, picked);
    }

    // Flux: "multiple — listbox variant only". The browser's own select holds one answer here.
    /// <inheritdoc />
    private protected override Component NativeSelect(UiWithField field, string look, IReadOnlyList<Component?> options) =>
        throw new InvalidOperationException(
            "A Ui.Select that holds a collection is the listbox's: add .Listbox (and .Multiple() to pick several).");
}
