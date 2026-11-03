using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux's <c>flux:select.option</c>: one answer of a <see cref="UiSelect{T}" />, written as its child.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.SelectOption.Value(country.Code)[country.Name]</c>. The value is the select's own type — the
///     select says so when it is drawn, since an option cannot know which select it will be handed to —
///     and an option with no <see cref="Value" /> stands for its own words, as Flux's does.
///     </para>
///     <para>
///     The select draws it: an <c>&lt;option&gt;</c> in the native variant, a row of the listbox otherwise.
///     <see cref="Icon" />, <see cref="Avatar" />, <see cref="Description" /> and content other than text are
///     the listbox's and the combobox's; a native <c>&lt;option&gt;</c> holds text and nothing else.
///     </para>
/// </remarks>
public sealed partial class UiSelectOption : Component
{
    /// <summary>What picking this option stores. Unset, the option's own text.</summary>
    public object? Value { get; set; }

    /// <summary>The option's words, in place of children.</summary>
    public string? Label { get; set; }

    /// <summary>What the select shows once this option is picked, when that is not its <see cref="Label" />.</summary>
    public string? SelectedLabel { get; set; }

    /// <summary>More words a search finds this option by, without showing them.</summary>
    public string? Keywords { get; set; }

    /// <summary>An icon before the words.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>Which drawing of <see cref="Icon" />. <see cref="Ui.IconVariant.Mini" /> when unset.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>Classes for the icon.</summary>
    public string? IconClass { get; set; }

    /// <summary>
    ///     An avatar before the words; it wins over <see cref="Icon" />. A string is the picture's address, Flux's
    ///     <c>avatar</c>. A <c>Ui.Avatar</c> of your own is Flux's <c>avatar:*</c>, the props handed to that avatar —
    ///     <c>.Avatar(Ui.Avatar.ColorAuto())</c>; what it leaves unset is the option's: extra small, round, named by its words.
    /// </summary>
    public Component? Avatar { get; set; }

    /// <summary>A line under the words, in the list only: the select does not repeat it once picked.</summary>
    public string? Description { get; set; }

    /// <summary>True for an option that is shown and cannot be picked.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Classes for the call site, added to the option's own.</summary>
    public string? Class { get; set; }

    /// <summary>False for an option a search never hides: the pillbox's <c>filterable</c>.</summary>
    internal bool? Filterable { get; private set; }

    /// <summary>States whether a search may hide this option. The kit's own chain step, for <c>Ui.PillboxOption</c>.</summary>
    /// <param name="filterable">False to keep the option whatever is typed.</param>
    internal UiSelectOption FilteredWhen(bool? filterable)
    {
        if (Filterable != filterable)
        {
            Filterable = filterable;
            BuilderRuntime.MarkChanged(this);
        }

        return this;
    }

    /// <summary>The words this option is read, searched and typed to by.</summary>
    internal string Text => Label ?? UiSelectText.Of(Children);

    /// <summary>The words the select shows when this option is the one picked.</summary>
    internal string SelectedText => SelectedLabel ?? Text;

    /// <summary>Whether what the option shows is more than its words.</summary>
    internal bool HasCustomContent => Label is null && Children is not null && !UiSelectText.IsTextOnly(Children);

    /// <summary>What the option posts and is told apart by: its value as a form writes it.</summary>
    internal string FormValue => Value is null ? Text : BindingHelpers.FormatValue(Value);

    // Outside a select there is no list to be a row of: the browser's own option.
    /// <inheritdoc />
    protected override Component? Render() =>
        Option.Value(FormValue).Disabled(Disabled == true).Class(Class)[Text];
}
