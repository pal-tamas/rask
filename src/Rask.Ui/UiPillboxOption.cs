namespace Rask;

/// <summary>
///     Flux's <c>flux:pillbox.option</c>: one answer of a <see cref="UiPillbox{T}" />, written as its child.
/// </summary>
/// <remarks>
///     <c>Ui.PillboxOption.Value(tag.Id)[tag.Name]</c>. The value is the pillbox's own type — the pillbox says
///     so when it is drawn — and an option with no <see cref="Value" /> stands for its own words. Its content
///     can be more than words: an icon beside them, as on Flux's page.
/// </remarks>
public sealed partial class UiPillboxOption : Component
{
    /// <summary>What picking this option stores. Unset, the option's own text.</summary>
    public object? Value { get; set; }

    /// <summary>The option's words, in place of children.</summary>
    public string? Label { get; set; }

    /// <summary>What the option's pill says, when that is not its <see cref="Label" />.</summary>
    public string? SelectedLabel { get; set; }

    /// <summary>True for an option that is shown and cannot be picked.</summary>
    public bool? Disabled { get; set; }

    /// <summary>False for an option the search never hides.</summary>
    public bool? Filterable { get; set; }

    /// <summary>Classes for the call site, added to the option's own.</summary>
    public string? Class { get; set; }

    // The pillbox draws the row; on its own it is nothing.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
