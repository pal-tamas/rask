using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux's <c>flux:pillbox</c>: several answers out of a list of options, each shown as a pill that can be
///     taken off again.
/// </summary>
/// <typeparam name="T">The type of one answer: what each option's <c>Value</c> is.</typeparam>
/// <remarks>
///     <para>
///     <c>Ui.Pillbox.Bind(() =&gt; model.Tags)</c> two-way binds a collection — a <c>List&lt;T&gt;</c>, a
///     <c>T[]</c>, a <c>HashSet&lt;T&gt;</c> — and infers <typeparamref name="T" /> from it;
///     <c>Ui.Pillbox.Values(tags).OnChange(…)</c> leaves the answers with the page. Options are children:
///     </para>
///     <code>
///     Ui.Pillbox.Bind(() =&gt; model.Tags).Placeholder("Choose tags...")[
///         tags.Select(tag =&gt; Ui.PillboxOption.Value(tag.Id)[tag.Name])
///     ]
///     </code>
///     <para>
///     It is the list <c>Ui.Select</c> draws, under another trigger: no script, a native <c>popover</c> placed
///     by CSS anchor positioning, the cursor, the search and the picking in C#. <c>.Searchable()</c> puts a
///     search field over the list; <c>.Combobox</c> puts the input among the pills.
///     </para>
/// </remarks>
public sealed partial class UiPillbox<T> : Component, IFormControl<ICollection<T>>
{
    /// <summary>The answers, when the page holds them. Pair it with <see cref="OnChange" />.</summary>
    public ICollection<T>? Value { get; set; }

    /// <summary>Called with a new collection of answers when the page holds them.</summary>
    public Callback<ICollection<T>> OnChange { get; set; }

    /// <summary>The collection the pillbox reads and writes: Flux's <c>wire:model</c>.</summary>
    public Expression<Func<ICollection<T>>>? Bind { get; set; }

    /// <summary>A rule for this field, run with the form's own.</summary>
    public Validator<ICollection<T>>? Validate { get; set; }

    /// <summary>Called after a pick has been written to the bound collection and validated.</summary>
    public Callback<ICollection<T>> AfterBind { get; set; }

    /// <summary>Shown while nothing is picked.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Flux's <c>label</c>: wraps the pillbox in a field with this label over it.</summary>
    public string? Label { get; set; }

    /// <summary>Flux's <c>description</c>: help text between the label and the pillbox.</summary>
    public string? Description { get; set; }

    /// <summary>How tall the pillbox is while it holds one line of pills.</summary>
    public Ui.PillboxSize? Size { get; set; }

    /// <summary>The pills alone, or an input among them that narrows the list.</summary>
    public Ui.PillboxVariant? Variant { get; set; }

    /// <summary>True for a search field over the options.</summary>
    public bool? Searchable { get; set; }

    /// <summary>What the search field says while it is empty. "Search..." when unset.</summary>
    public string? SearchPlaceholder { get; set; }

    /// <summary>
    ///     False to leave every option in the list whatever is typed: the page filters, answering the input's
    ///     <c>OnInput</c> by rendering the options that match.
    /// </summary>
    public bool? Filter { get; set; }

    /// <summary>True for a pillbox that cannot be opened or changed.</summary>
    public bool? Disabled { get; set; }

    /// <summary>True for error styling the form did not ask for. A bound pillbox is invalid while its member holds a message.</summary>
    public bool? Invalid { get; set; }

    /// <summary>Classes for the call site, added to the pillbox's own.</summary>
    public string? Class { get; set; }

    /// <summary>The control's id. Unset, it is derived from the bound member or the label.</summary>
    public string? Id { get; set; }

    /// <summary>False to leave the bound member's message to a <c>Ui.Error</c> placed elsewhere.</summary>
    public bool? ShowValidation { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        UiPillboxTrigger? trigger = null;
        UiPillboxSearch? search = null;
        List<Component?> parts = [];
        foreach (var child in Children ?? [])
        {
            switch (child)
            {
                case UiPillboxTrigger slot:
                    trigger = slot;
                    break;
                case UiPillboxSearch slot:
                    search = slot;
                    break;
                default:
                    parts.Add(UiPillboxParts.Translate(child));
                    break;
            }
        }

        if (search is not null || SearchPlaceholder is not null)
        {
            parts.Add(UiPillboxParts.Search(search, SearchPlaceholder));
        }

        var list = Bind is { } bind
            ? Ui.Select.Bind(bind).Validate(Validate).AfterBind(AfterBind)
            : Ui.Select.Values(Value).OnChange(OnChange);

        return list
            .Listbox
            .Multiple()
            .Placeholder(Placeholder)
            .Label(Label)
            .Description(Description)
            .Size(Size == Ui.PillboxSize.Sm ? Ui.SelectSize.Sm : null)
            .Searchable(Searchable == true || search is not null)
            .Filter(Filter)
            .Disabled(Disabled)
            .Invalid(Invalid)
            .Id(Id)
            .ShowValidation(ShowValidation)
            .Class(Class)
            .AsPillbox(new UiPillboxFace { Combobox = Variant == Ui.PillboxVariant.Combobox, Trigger = trigger })[parts];
    }
}
