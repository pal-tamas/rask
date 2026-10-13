namespace Rask;

/// <summary>
///     Flux's <c>flux:pillbox.search</c>: the search field at the top of a searchable
///     <see cref="UiPillbox{T}" />'s list, written out to set its words or to hear what is typed.
/// </summary>
/// <remarks>
///     A child of the pillbox — Flux's <c>search</c> slot:
///     <c>Ui.Pillbox.Bind(() =&gt; m.Skills)[Ui.PillboxSearch.Placeholder("Filter skills..."), …]</c>.
///     <see cref="OnInput" /> is where Flux writes <c>wire:model.live</c>, for a list the page filters itself.
/// </remarks>
public sealed partial class UiPillboxSearch : Component
{
    /// <summary>Shown while the field is empty. "Search..." when unset.</summary>
    public string? Placeholder { get; set; }

    /// <summary>The icon at the start of the field. A magnifier when unset.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>False to leave out the button that empties the field. It is there when unset.</summary>
    public bool? Clearable { get; set; }

    /// <summary>The search, when the page holds it: set it to empty to clear what was typed.</summary>
    public string? Value { get; set; }

    /// <summary>Called with the field's text on every keystroke, or once typing has paused for <see cref="Debounce" />.</summary>
    public Callback<string> OnInput { get; set; }

    /// <summary>
    ///     How long typing must pause before <see cref="OnInput" /> hears the text, once:
    ///     <c>Ui.PillboxSearch.OnInput(Search).Debounce(250.Milliseconds)</c>, for a search that asks a database.
    ///     Flux's <c>wire:model.live.debounce.250ms</c>. The browser counts the pause and sends nothing
    ///     meanwhile. A pick made during it is of the list as drawn, and the text typed since is dropped.
    ///     <see cref="TimeSpan.Zero" /> is every keystroke.
    /// </summary>
    public TimeSpan? Debounce { get; set; }

    /// <summary>Classes for the call site, added to the field's own.</summary>
    public string? Class { get; set; }

    // The pillbox draws the field; on its own it is nothing.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
