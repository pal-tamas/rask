using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux's <c>flux:select</c>, whatever it holds: the props, the three variants and the open list.
///     <see cref="UiSelect{T}" /> holds one answer and <see cref="UiSelectMultiple{T}" /> a collection of them;
///     both are <c>Ui.Select</c> at the call site.
/// </summary>
/// <typeparam name="T">The type of one answer: what an option's <c>Value</c> is.</typeparam>
/// <remarks>
///     <para>
///     Options are children, as in Flux: <c>Ui.SelectOption.Value(c.Code)[c.Name]</c>, grouped with
///     <c>Ui.SelectGroup</c>. The default variant is the browser's own <c>&lt;select&gt;</c>;
///     <c>Listbox</c> is a button over a drawn list and <c>Combobox</c> a text input that filters one.
///     </para>
///     <para>
///     The drawn list ships no script. It is a native <c>popover</c> placed by CSS anchor positioning, and
///     where the reader is in it is the browser's, kept by the runtime (<c>data-rask-list</c>): the lit row,
///     the keys, type-ahead, whether it is open and the rows a search leaves send the page nothing. The page
///     renders the rows and hears a pick.
///     </para>
/// </remarks>
public abstract partial class UiSelectControl<T> : Component, IUiFormControl, IDeferredBind, IUiLockable
{
    private readonly int _instance = UiInstanceCounter.Next();

    /// <summary>Flux's <c>label</c>: wraps the select in a field with this label over it.</summary>
    public string? Label { get; set; }

    /// <summary>Flux's <c>description</c>: help text between the label and the select.</summary>
    public string? Description { get; set; }

    /// <summary>Flux's <c>description:trailing</c>: help text under the select instead.</summary>
    public string? DescriptionTrailing { get; set; }

    /// <summary>Flux's <c>badge</c>: a word beside the label, such as "Optional".</summary>
    public string? Badge { get; set; }

    /// <summary>Shown while nothing is picked.</summary>
    public string? Placeholder { get; set; }

    /// <summary>How tall the select is.</summary>
    public Ui.SelectSize? Size { get; set; }

    /// <summary>The browser's own select, a button over a drawn list, or an input that filters one.</summary>
    public Ui.SelectVariant? Variant { get; set; }

    /// <summary>True to pick several options. The listbox's, over a select that holds a collection.</summary>
    public bool? Multiple { get; set; }

    /// <summary>A checkmark beside the picked row, or a checkbox beside every row. The listbox's.</summary>
    public Ui.SelectIndicator? Indicator { get; set; }

    /// <summary>
    ///     False to leave every option in the list whatever is typed: the page filters, answering the search's
    ///     <c>OnInput</c> by rendering the options that match.
    /// </summary>
    public bool? Filter { get; set; }

    /// <summary>True for a search field over the listbox's options.</summary>
    public bool? Searchable { get; set; }

    /// <summary>
    ///     The options for what is typed, answered by the page — from a database, or from more rows than a list should
    ///     draw: <c>.Search(async typed =&gt; (await Partner.Named(typed, 20)).Select(p =&gt; Ui.SelectOption.Value(p.Id)[p.Name]))</c>.
    /// </summary>
    /// <remarks>
    ///     Flux's backend search — <c>:filter="false"</c> with <c>wire:model.live.debounce</c> on the search — held by the
    ///     select itself. Asked for <c>""</c> when the select mounts, then once typing has paused (150 ms, or the search
    ///     slot's <c>Debounce</c>), and again whenever the page hands over another search (a lambda over rows it has
    ///     read since); the list says it is loading while an answer is on its way, and the answer replaces the
    ///     rows. The options written as children are the rows before the first answer — the one the value holds, say, so
    ///     the select can name it. A picked option stays first in the list while a search does not find it. A listbox
    ///     with a search field unless <c>Combobox</c> says otherwise.
    /// </remarks>
    public Fn<string, Task<IEnumerable<Component?>>>? Search { get; set => UiAskedSearch.Take(this, ref field, value); }

    /// <summary>What the list says when a search matches nothing. "No results found" when unset.</summary>
    public string? Empty { get; set; }

    /// <summary>True for a button that clears the answer, shown while there is one.</summary>
    public bool? Clearable { get; set; }

    /// <summary>Words that stay in the listbox's button before the picked option: "Compare to".</summary>
    public string? Prefix { get; set; }

    /// <summary>What follows the count once several options are picked. "selected" when unset.</summary>
    public string? SelectedSuffix { get; set; }

    /// <summary>When a search empties itself: at every pick, or only when the list closes.</summary>
    public Ui.SelectClear? Clear { get; set; }

    /// <summary>Which side of the select the list opens on. Below when unset.</summary>
    public Ui.Position? Position { get; set; }

    /// <summary>Which edge of the select the list lines up with. Its start when unset.</summary>
    public Ui.Align? Align { get; set; }

    /// <summary>Classes for the open list — a width, such as <c>min-w-72</c>. Flux's <c>options:class</c>.</summary>
    public string? OptionsClass { get; set; }

    /// <summary>True for a select that cannot be opened or changed.</summary>
    public bool? Disabled { get; set; }

    /// <inheritdoc />
    bool IUiLockable.Locked => Disabled == true;

    /// <summary>
    ///     Writes the model as soon as it is chosen, and validates then:
    ///     <c>Ui.Select.Bind(() =&gt; m.Plan).Live()</c>. Flux's <c>wire:model.live</c>. Without it the choice is
    ///     written with the next action, and travels with that — Flux's <c>wire:model</c>.
    /// </summary>
    public bool? Live { get; set; }

    /// <summary>True for error styling the form did not ask for. A bound select is invalid while its member holds a message.</summary>
    public bool? Invalid { get; set; }

    /// <summary>Classes for the call site, added to the select's own.</summary>
    public string? Class { get; set; }

    /// <summary>The control's id. Unset, it is derived from the bound member or the label.</summary>
    public string? Id { get; set; }

    /// <summary>False to leave the bound member's message to a <c>Ui.Error</c> placed elsewhere.</summary>
    public bool? ShowValidation { get; set; }

    /// <summary>The name the answer is posted under in a form.</summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    // A select nothing names — no Id, no bound member, no label — has an id of its own, not one they all share.
    string IUiFieldControl.ControlId => Id is null && Bound is null && Label is null
        ? UiFieldId.Own(_instance)
        : UiFieldId.Derive(Id, Bound, Label);

    /// <inheritdoc />
    LambdaExpression? IUiFieldControl.Bound => Bound;

    /// <summary>The expression the select is bound with, when it is.</summary>
    private protected abstract LambdaExpression? Bound { get; }

    // What a refusal names the select by: the member it is bound to, or its Name while controlled.
    private ExpressionAccessor.Accessor? Accessor => Bound is { } bound ? ExpressionAccessor.Parse(bound) : null;

    /// <summary>Whether the select holds a collection of answers rather than one.</summary>
    private protected abstract bool HoldsMany { get; }

    private string Prefixed => "uisel-" + _instance.ToString(CultureInfo.InvariantCulture);

    private string PanelId => Prefixed + "-options";

    private Ui.SelectSize Height => Size ?? Ui.SelectSize.Base;

    private bool IsMultiple => HoldsMany && Multiple != false;

    // A select the page searches is drawn: a listbox with a search field, unless it says it is a combobox.
    private Ui.SelectVariant Shape => Variant ?? (Asked is null && _choosing is null ? Ui.SelectVariant.Default : Ui.SelectVariant.Listbox);

    // A pillbox with an input among its pills is typed into there, and needs no field over its list.
    private bool HasSearchField => Shape == Ui.SelectVariant.Listbox && (SearchesByItself || (Asked is not null && !PillsCombobox));

    private bool IsCombobox => Shape == Ui.SelectVariant.Combobox;

    /// <summary>Whether something is typed to narrow the list: a combobox, a search field, a pillbox's own input.</summary>
    private bool Searches => IsCombobox || HasSearchField || PillsCombobox;

    /// <summary>What is picked now. Called once a render; a bound select registers its validator here.</summary>
    // The form the bound member belongs to, as the last render found it: a pick is committed from a handler,
    // which runs outside any render and would find none.
    private protected EditContext? Form { get; set; }

    private protected abstract IReadOnlyList<T> Current();

    /// <summary>Writes a new answer: to the bound member, or to the page through its callback.</summary>
    /// <param name="picked">Every picked value. One or none for a select that holds one.</param>
    private protected abstract Task CommitAsync(IReadOnlyList<T> picked);

    /// <summary>The browser's own <c>&lt;select&gt;</c>, opened on the binding and holding <paramref name="options" />.</summary>
    /// <param name="field">The field around the control: its id and ARIA.</param>
    /// <param name="look">The control's classes.</param>
    /// <param name="options">The <c>&lt;option&gt;</c>s and <c>&lt;optgroup&gt;</c>s.</param>
    private protected abstract Component NativeSelect(UiWithField field, string look, IReadOnlyList<Component?> options);

    /// <summary>What the select's button says for <paramref name="value" />: its option's words, or the value itself.</summary>
    private protected string Picked(T value) =>
        Parts.Read(Written).Rows.FirstOrDefault(row => EqualityComparer<T>.Default.Equals(row.Value, value))?.Option.SelectedText
        ?? Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Multiple == true && !HoldsMany)
        {
            throw new InvalidOperationException(
                "Ui.Select.Multiple() picks several options, so it holds a collection: open it with "
                + ".Bind(() => model.Tags) over a collection, or with .Values(…).");
        }

        var field = UiWithField.For(this);
        if (Shape == Ui.SelectVariant.Default && Pills is null)
        {
            return field.Wrap(Native(field, Parts.Read(Written)));
        }

        var picked = Current();

        return field.Wrap(Drawn(field, Parts.Read(Offered(picked)), picked));
    }
}
