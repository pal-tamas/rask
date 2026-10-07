using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux's <c>flux:select</c>: one answer out of a list of options.
/// </summary>
/// <typeparam name="T">The type of the answer: what each option's <c>Value</c> is.</typeparam>
/// <remarks>
///     <para>
///     <c>Ui.Select.Bind(() =&gt; model.Country)</c> two-way binds a member and infers <typeparamref name="T" />
///     from it; <c>Ui.Select.Value(country).OnChange(c =&gt; …)</c> leaves the answer with the page. Options are
///     children:
///     </para>
///     <code>
///     Ui.Select.Bind(() =&gt; model.Country).Label("Country").Placeholder("Choose…")[
///         countries.Select(c =&gt; Ui.SelectOption.Value(c.Code)[c.Name])
///     ]
///     </code>
///     <para>
///     The browser's own <c>&lt;select&gt;</c> unless <c>.Listbox</c> or <c>.Combobox</c> asks for a drawn
///     list. A <see cref="UiSelectControl{T}.Label" /> or a description draws the field around it, and a
///     bound select shows its member's validation message there.
///     </para>
///     <para>
///     Bound to a collection — <c>Ui.Select.Bind(() =&gt; model.Tags).Listbox.Multiple()</c> — the same chain
///     builds <see cref="UiSelectMultiple{T}" />.
///     </para>
/// </remarks>
public sealed partial class UiSelect<T> : UiSelectControl<T>, IFormControl<T>
{
    /// <summary>The answer, when the page holds it. Pair it with <see cref="OnChange" />.</summary>
    public T? Value { get; set; }

    /// <summary>Called with the new answer when the page holds it.</summary>
    public Callback<T> OnChange { get; set; }

    /// <summary>The member the select reads and writes: Flux's <c>wire:model</c>.</summary>
    public Expression<Func<T>>? Bind { get; set; }

    /// <summary>A rule for this field, run with the form's own.</summary>
    public Validator<T>? Validate { get; set; }

    /// <summary>Called after a pick has been written to the bound member and validated.</summary>
    public Callback<T> AfterBind { get; set; }

    /// <inheritdoc />
    private protected override LambdaExpression? Bound => Bind;

    /// <inheritdoc />
    private protected override bool HoldsMany => false;

    /// <inheritdoc />
    private protected override IReadOnlyList<T> Current() =>
        UiFormCommit.Resolve(this).Current is { } current ? [current] : [];

    /// <inheritdoc />
    private protected override Task CommitAsync(IReadOnlyList<T> picked)
    {
        var (accessor, context, _) = UiFormCommit.Resolve(this);

        return UiFormCommit.CommitAsync(this, accessor, context, picked.Count > 0 ? picked[0] : default!);
    }

    // Bind and Value are the two openings of Core's select, and both hand back the same element.
    /// <inheritdoc />
    private protected override Component NativeSelect(UiWithField field, string look, IReadOnlyList<Component?> options)
    {
        var select = Bind is { } bind
            ? Select.Bind(bind).Validate(Validate).AfterBind(AfterBind)
            : Select.Value(Value).OnChange(OnChange);

        return select
            .Id(field.ControlId)
            .Name(Name)
            .Disabled(Disabled == true)
            .Aria(field.Aria)
            .Attributes(NativeMarks(field.Invalid))
            .Class(look)[options];
    }
}
