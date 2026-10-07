namespace Rask;

/// <summary>
///     Flux's <c>flux:pillbox.input</c>: the input among the pills of a combobox <see cref="UiPillbox{T}" />,
///     written out to hear what is typed.
/// </summary>
/// <remarks>
///     <para>
///     A child of the pillbox — Flux's <c>input</c> slot. <see cref="Value" /> and <see cref="OnInput" /> are
///     where Flux writes <c>wire:model="search"</c>: the page holds what was typed, to make an option of it or
///     to answer it with the options that match.
///     </para>
///     <code>
///     Ui.Pillbox.Bind(() =&gt; m.Tags).Combobox[
///         Ui.PillboxInput.Value(_search).OnInput(text =&gt; _search = text).Placeholder("Choose tags..."),
///         tags.Select(tag =&gt; Ui.PillboxOption.Key(tag.Id).Value(tag.Id)[tag.Name]),
///         Ui.PillboxOptionCreate.MinLength(2).OnClick(CreateTag)["Create new \"", _search, "\""]
///     ]
///     </code>
/// </remarks>
public sealed partial class UiPillboxInput : Component
{
    /// <summary>Shown while nothing is picked and nothing is typed.</summary>
    public string? Placeholder { get; set; }

    /// <summary>True for error styling: what Flux draws while the page's check of what was typed has failed.</summary>
    public bool? Invalid { get; set; }

    /// <summary>What is typed, when the page holds it: set it to empty to clear the input.</summary>
    public string? Value { get; set; }

    /// <summary>Called with the input's text on every keystroke.</summary>
    public Callback<string> OnInput { get; set; }

    /// <summary>Classes for the call site, added to the input's own.</summary>
    public string? Class { get; set; }

    // The pillbox draws the input; on its own it is nothing.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
