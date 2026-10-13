using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux's <c>flux:autocomplete</c>: a text input that suggests what to type, and writes the picked
///     suggestion into itself.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.Autocomplete.Bind(() =&gt; model.State).Label("State of residence")[states.Select(s =&gt; Ui.AutocompleteItem[s])]</c>.
///     What it holds is the text: an item has no value of its own, and what is typed need not be an item. To
///     show a name and store an id, use <c>Ui.Select.Combobox</c>.
///     </para>
///     <para>
///     It is Flux's input — every prop of <see cref="UiInput{T}" /> that Flux documents here is handed to one —
///     over a native <c>popover</c> placed by CSS anchor positioning. No script of its own: the lit row, the
///     filter, whether the list is open and the pick that writes an item into the input are the browser's,
///     kept by the runtime (<c>data-rask-list</c>), and send the page nothing. The page hears the text the way
///     any input's is heard: with the next action when bound, at once with <c>.Live()</c>, and on a pick, a
///     clear or leaving the input when it holds the value itself.
///     </para>
/// </remarks>
public sealed partial class UiAutocomplete : Component, IFormControl<string>, IDeferredBind
{
    private readonly int _instance = UiInstanceCounter.Next();

    /// <summary>The text, when the page holds it. Pair it with <see cref="OnChange" />.</summary>
    public string? Value { get; set; }

    /// <summary>Called with the text when it is picked, cleared, or typed and left.</summary>
    public Callback<string> OnChange { get; set; }

    /// <summary>The member the input reads and writes: Flux's <c>wire:model</c>.</summary>
    public Expression<Func<string>>? Bind { get; set; }

    /// <summary>A rule for this field, run with the form's own.</summary>
    public Validator<string>? Validate { get; set; }

    /// <summary>Called after the text has been written to the bound member and validated.</summary>
    public Callback<string> AfterBind { get; set; }

    /// <summary>
    ///     Writes the model as soon as it is chosen, and validates then:
    ///     <c>Ui.Autocomplete.Bind(() =&gt; m.City).Live()</c>. Flux's <c>wire:model.live</c>. Without it the choice is
    ///     written with the next action, and travels with that — Flux's <c>wire:model</c>.
    /// </summary>
    public bool? Live { get; set; }

    /// <summary>The input's type. Text when unset.</summary>
    public InputType? Type { get; set; }

    /// <summary>Flux's <c>label</c>: wraps the input in a field with this label over it.</summary>
    public string? Label { get; set; }

    /// <summary>Flux's <c>description</c>: help text between the label and the input.</summary>
    public string? Description { get; set; }

    /// <summary>Shown while the input is empty.</summary>
    public string? Placeholder { get; set; }

    /// <summary>How tall the input is.</summary>
    public Ui.AutocompleteSize? Size { get; set; }

    /// <summary>Outlined, or filled with no border.</summary>
    public Ui.AutocompleteVariant? Variant { get; set; }

    /// <summary>True for an input that cannot be typed into or opened.</summary>
    public bool? Disabled { get; set; }

    /// <summary>True for an input that shows its text and does not take any.</summary>
    public bool? ReadOnly { get; set; }

    /// <summary>True for error styling the form did not ask for. A bound input is invalid while its member holds a message.</summary>
    public bool? Invalid { get; set; }

    /// <summary>For a file input: several files.</summary>
    public bool? Multiple { get; set; }

    /// <summary>A pattern the text is laid into: <c>99/99/9999</c>.</summary>
    public string? Mask { get; set; }

    /// <summary>An icon, or content, at the start of the input.</summary>
    public UiInputIcon? Icon { get; set; }

    /// <summary>An icon, or content, at the end of the input.</summary>
    public UiInputIcon? IconTrailing { get; set; }

    /// <summary>A keyboard shortcut shown at the end of the input.</summary>
    public string? Kbd { get; set; }

    /// <summary>True for a button that empties the input, shown while it has text.</summary>
    public bool? Clearable { get; set; }

    /// <summary>True for a button that copies what the input holds, and shows a tick for two seconds.</summary>
    public bool? Copyable { get; set; }

    /// <summary>For a password input: true for a button that shows what was typed.</summary>
    public bool? Viewable { get; set; }

    /// <summary>What the input is drawn as. The input when unset.</summary>
    public Ui.AutocompleteAs? As { get; set; }

    /// <summary>Classes for the open list — a height, such as <c>max-h-80</c>. Flux's <c>container:class</c>.</summary>
    public string? ContainerClass { get; set; }

    /// <summary>Classes for the <c>&lt;input&gt;</c> itself rather than the box around it.</summary>
    public string? InputClass { get; set; }

    /// <summary>Classes for the call site, added to the input box's own.</summary>
    public string? Class { get; set; }

    /// <summary>The input's id. Unset, it is derived from the bound member or the label.</summary>
    public string? Id { get; set; }

    /// <summary>False to leave the bound member's message to a <c>Ui.Error</c> placed elsewhere.</summary>
    public bool? ShowValidation { get; set; }

    private string Prefixed => "uiac-" + _instance.ToString(CultureInfo.InvariantCulture);

    private string PanelId => Prefixed + "-items";

    /// <inheritdoc />
    protected override Component? Render()
    {
        // Bind and Value are the two openings of the kit's input, and both hand back the same control.
        var opened = Bind is { } bind
            ? Ui.Input.Bind(bind).Validate(Validate).AfterBind(AfterBind).Live(Live)
            : Ui.Input.Value(Value ?? string.Empty).OnChange(OnChange);
        var input = opened
            .Id(UiFieldId.Derive(Id, Bind, Label))
            .Type(Type)
            .Label(Label)
            .Description(Description)
            .Placeholder(Placeholder)
            .Size(UiAutocompleteLook.InputSize(Size))
            .Variant(Variant == Ui.AutocompleteVariant.Filled ? Ui.InputVariant.Filled : null)
            .Disabled(Disabled)
            .ReadOnly(ReadOnly)
            .Invalid(Invalid)
            .Multiple(Multiple)
            .Mask(Mask)
            .Icon(Icon)
            .IconTrailing(IconTrailing)
            .Kbd(Kbd)
            .Clearable(Clearable)
            .Copyable(Copyable)
            .Viewable(Viewable)
            .As(As == Ui.AutocompleteAs.Button ? Ui.InputAs.Button : null)
            .InputClass(InputClass)
            .ShowValidation(ShowValidation)
            .Class(Class)
            .HostedBy(new UiInputHost { Bound = Bind, AnchorName = Prefixed, Aria = InputAria });

        return Div.Class(UiAutocompleteLook.Root).Attributes(UiAutocompleteLook.RootMarks)[input, Popup()];
    }

    // Rendered shut, always: `aria-expanded` and the row `aria-activedescendant` names are written by the
    // browser as the list opens and its cursor moves.
    private Dictionary<string, string?> InputAria => new(StringComparer.Ordinal)
    {
        ["controls"] = PanelId,
        ["autocomplete"] = "list",
        ["haspopup"] = "listbox",
        ["expanded"] = "false",
    };

    // `aria-multiselectable` on a list that holds one answer is Flux's: its autocomplete is its select's
    // element, and that is what it writes.
    private Component Popup()
    {
        var items = (Children ?? []).OfType<UiAutocompleteItem>().ToList();

        return Div
            .Id(PanelId)
            .Popover(Popover.Auto)
            .Role("listbox")
            .TabIndex(-1)
            .Class(UiClass.Compose(UiListboxLook.Box, UiListboxLook.Scrolls, UiAutocompleteLook.Items, ContainerClass))
            .Aria("multiselectable", "true")
            .Attributes(PopupMarks())[
            items.Select(Row),
            // Flux's `ui-empty`: a place for a message it documents no way to fill. Kept, so the list is its list.
            Div.Class(UiAutocompleteLook.Empty).Attributes(items.Count > 0 ? EmptyHidden : EmptyShown)
        ];
    }

    private static readonly Dictionary<string, string?> EmptyShown = new(StringComparer.Ordinal) { ["data-rask-list-empty"] = null };

    private static readonly Dictionary<string, string?> EmptyHidden = new(EmptyShown, StringComparer.Ordinal) { ["data-hidden"] = null };

    // Opened and shut in the browser, by the input and the keys (rask-list.ts): the page says nothing of it.
    private Dictionary<string, string?> PopupMarks() => new(StringComparer.Ordinal)
    {
        ["data-ui-autocomplete-items"] = null,
        // While the list is open the page behind it neither scrolls nor takes the pointer, as on Flux.
        ["data-rask-lock"] = null,
        ["style"] = UiAnchor.Under(Prefixed, null, null),
        ["data-rask-place"] = UiAnchor.Place(null, null),
    };

    // A row has no handler: a press, or Enter on the lit row, writes its words into the input in the browser,
    // and the input tells the page as it tells it of anything typed. Which row was picked last is marked there
    // too (`aria-selected`), as Flux keeps it marked whatever is typed afterwards.
    private Component Row(UiAutocompleteItem item, int index)
    {
        var off = item.Disabled == true;
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal) { ["data-ui-autocomplete-item"] = null };
        if (item.Children is { } content && !UiSelectText.IsTextOnly(content))
        {
            // The words the row is found by and writes, where it draws more than them.
            marks["data-rask-list-text"] = UiSelectText.Of(content);
        }

        return Div
            .Key(item.Key)
            .Id(UiSelectNav.OptId(Prefixed, index))
            .Role("option")
            .Class(UiClass.Compose(UiAutocompleteLook.Item, item.Class))
            .Aria(UiOptionAria.For(off, selected: false))
            .Attributes(marks)[
            item.Children ?? []
        ];
    }
}
