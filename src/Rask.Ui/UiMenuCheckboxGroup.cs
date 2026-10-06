namespace Rask;

/// <summary>
/// A run of <see cref="UiMenuCheckbox" /> rows that belong together. Flux UI's <c>flux:menu.checkbox.group</c>.
/// </summary>
/// <remarks>
/// A <c>group</c> to assistive tech, and nothing to the eye. Flux binds the group's <c>wire:model</c> to an
/// array of the checked rows; in Rask each row binds its own flag — <c>.Bind(() =&gt; filter.Draft)</c> — which is
/// what a set of independent switches is.
/// </remarks>
public sealed partial class UiMenuCheckboxGroup : Component
{
    /// <summary>Keeps the menu open after any row in the group is picked.</summary>
    public bool? KeepOpen { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Role("group").Class(UiClass.Compose("inline", Class)).Data(KeepOpen == true ? KeepsOpen : Group)[Children ?? []];

    private static readonly Dictionary<string, string?> Group =
        new(StringComparer.Ordinal) { ["ui-menu-checkbox-group"] = "" };

    private static readonly Dictionary<string, string?> KeepsOpen =
        new(StringComparer.Ordinal) { ["ui-menu-checkbox-group"] = "", ["rask-keep-open"] = "" };
}
