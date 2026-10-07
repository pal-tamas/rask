namespace Rask;

/// <summary>
///     Flux's <c>flux:autocomplete.item</c>: one suggestion of a <see cref="UiAutocomplete" />, written as its
///     child.
/// </summary>
/// <remarks>
///     <c>Ui.AutocompleteItem["Alabama"]</c>. Its words are what picking it writes into the input, and what the
///     typed text is matched against.
/// </remarks>
public sealed partial class UiAutocompleteItem : Component
{
    /// <summary>True for an item that is shown and cannot be picked.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Classes for the call site, added to the item's own.</summary>
    public string? Class { get; set; }

    // The autocomplete draws the row; on its own it is nothing.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
